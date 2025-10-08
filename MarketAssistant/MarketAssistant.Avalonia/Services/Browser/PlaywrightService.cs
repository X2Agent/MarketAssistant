using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using MarketAssistant.Infrastructure;
using MarketAssistant.Services.Settings;
using IBrowser = Microsoft.Playwright.IBrowser;

namespace MarketAssistant.Avalonia.Services.Browser;

/// <summary>
/// Playwright服务，用于管理Playwright和Browser实例
/// </summary>
public class PlaywrightService : IAsyncDisposable
{
    // 配置常量
    private const int MaxConcurrentPages = 5;
    private const int DefaultTimeoutSeconds = 30;
    private const int HealthCheckIntervalMinutes = 5;
    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly string[] BrowserArgs = {
        "--disable-gpu",
        "--disable-extensions",
        "--disable-dev-shm-usage",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-networking",
        "--disable-background-timer-throttling"
    };

    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<PlaywrightService>? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _pageLock = new(MaxConcurrentPages, MaxConcurrentPages);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private DateTime _lastHealthCheck = DateTime.MinValue;

    public PlaywrightService(IUserSettingService userSettingService, ILogger<PlaywrightService>? logger)
    {
        _userSettingService = userSettingService;
        _logger = logger;
    }

    /// <summary>
    /// 获取Browser实例，如果尚未初始化则进行初始化
    /// </summary>
    /// <returns>Browser实例</returns>
    private async Task<IBrowser> GetBrowserAsync()
    {
        // 定期健康检查
        await PerformHealthCheckIfNeeded();

        // 检查现有浏览器是否仍然有效
        if (_browser != null && !_browser.IsConnected)
        {
            _browser = null;
        }

        if (_browser != null)
        {
            return _browser;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_browser != null && _browser.IsConnected)
            {
                return _browser;
            }

            await InitializeBrowserAsync();
            return _browser!;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 执行需要Page的操作，自动管理Page生命周期和并发控制
    /// </summary>
    public async Task<T> ExecuteWithPageAsync<T>(Func<IPage, Task<T>> action, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        cancellationToken.ThrowIfCancellationRequested();
        await _pageLock.WaitAsync(cancellationToken);
        try
        {
            return await ExecutePageOperationAsync(action, timeout.Value, cancellationToken);
        }
        finally
        {
            _pageLock.Release();
        }
    }

    /// <summary>
    /// 执行具体的页面操作
    /// </summary>
    private async Task<T> ExecutePageOperationAsync<T>(Func<IPage, Task<T>> action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var browser = await GetBrowserAsync();

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BypassCSP = true,
            UserAgent = DefaultUserAgent
        });

        await SetupResourceBlocking(context);

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout((float)timeout.TotalMilliseconds);

        return await action(page);
    }

    /// <summary>
    /// 设置资源阻止策略（实例方法以便使用日志）
    /// </summary>
    private async Task SetupResourceBlocking(IBrowserContext context)
    {
        await context.RouteAsync("**/*", async route =>
        {
            var resourceType = route.Request.ResourceType;
            if (resourceType is "image" or "media" or "font")
            {
                await SafeExecuteAsync(() => route.AbortAsync());
            }
            else
            {
                await SafeExecuteAsync(() => route.ContinueAsync());
            }
        });
    }

    /// <summary>
    /// 安全执行操作，忽略异常
    /// </summary>
    private async Task SafeExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "执行操作时发生错误: {Message}", e.Message);
        }
    }

    /// <summary>
    /// 执行需要Page的操作（无返回值版本）
    /// </summary>
    public async Task ExecuteWithPageAsync(Func<IPage, Task> action, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await ExecuteWithPageAsync(async page =>
        {
            await action(page);
            return 0; // 返回占位值
        }, timeout, cancellationToken);
    }

    /// <summary>
    /// 执行健康检查（如果需要）
    /// </summary>
    private async Task PerformHealthCheckIfNeeded()
    {
        var now = DateTime.UtcNow;
        if (now - _lastHealthCheck < TimeSpan.FromMinutes(HealthCheckIntervalMinutes))
        {
            return;
        }

        _lastHealthCheck = now;

        if (_browser?.IsConnected == false)
        {
            await CloseBrowserAsync();
        }
    }

    /// <summary>
    /// 初始化Playwright和Browser实例
    /// </summary>
    private async Task InitializeBrowserAsync()
    {
        if (_browser?.IsConnected == true)
        {
            return;
        }

        try
        {
            _logger?.LogInformation("初始化 Playwright...");

            _playwright ??= await Playwright.CreateAsync();

            var options = CreateBrowserOptions();
            var browserPath = _userSettingService.CurrentSetting.BrowserPath;

            if (!string.IsNullOrWhiteSpace(browserPath) && File.Exists(browserPath))
            {
                options.ExecutablePath = browserPath;
                _logger?.LogInformation("使用自定义浏览器路径: {Path}", browserPath);
            }
            else
            {
                _logger?.LogInformation("使用内置 Chromium");
                await InstallChromiumAsync();
            }

            _browser = await _playwright.Chromium.LaunchAsync(options);

            // 监听断连事件
            _browser.Disconnected += (_, _) =>
            {
                _logger?.LogWarning("浏览器连接断开");
                _browser = null;
            };

            _logger?.LogInformation("Playwright 初始化完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Playwright 初始化失败: {Message}", ex.Message);
            await CleanupFailedInitializationAsync();
            throw;
        }
    }

    /// <summary>
    /// 创建浏览器启动选项
    /// </summary>
    private static BrowserTypeLaunchOptions CreateBrowserOptions()
    {
        return new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = BrowserArgs
        };
    }

    /// <summary>
    /// 安装Chromium
    /// </summary>
    private static Task InstallChromiumAsync()
    {
        return Task.Run(() => Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }));
    }

    /// <summary>
    /// 清理失败的初始化状态
    /// </summary>
    private async Task CleanupFailedInitializationAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }
        });

        _playwright?.Dispose();
        _playwright = null;
    }

    /// <summary>
    /// 优雅关闭服务
    /// </summary>
    public async Task GracefulShutdownAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);

        // 等待所有正在进行的操作完成
        var waitStart = DateTime.Now;
        while (_pageLock.CurrentCount < MaxConcurrentPages && DateTime.Now - waitStart < timeout)
        {
            await Task.Delay(100);
        }

        await DisposeAsync();
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await CloseBrowserAsync();
        _initLock.Dispose();
        _pageLock.Dispose();
    }

    /// <summary>
    /// 关闭浏览器与Playwright实例
    /// </summary>
    private async Task CloseBrowserAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            await SafeExecuteAsync(async () =>
            {
                if (_browser != null)
                {
                    await _browser.CloseAsync();
                    _browser = null;
                }
            });

            _playwright?.Dispose();
            _playwright = null;
        }
        finally
        {
            _initLock.Release();
        }
    }
}