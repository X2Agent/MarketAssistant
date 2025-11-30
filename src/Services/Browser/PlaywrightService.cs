using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace MarketAssistant.Services.Browser;

/// <summary>
/// Playwright服务，用于管理Playwright和Browser实例
/// </summary>
public class PlaywrightService : IAsyncDisposable
{
    private const int MaxConcurrentPages = 5;
    private const int DefaultTimeoutSeconds = 30;
    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly string[] BrowserArgs = [
        "--disable-gpu",
        "--disable-extensions",
        "--disable-dev-shm-usage",
        "--no-first-run",
        "--no-default-browser-check"
    ];

    private static readonly string[] BlockedResourceTypes = ["image", "media", "font"];

    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<PlaywrightService>? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _pageLock = new(MaxConcurrentPages, MaxConcurrentPages);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _disposed;

    public PlaywrightService(IUserSettingService userSettingService, ILogger<PlaywrightService>? logger)
    {
        _userSettingService = userSettingService;
        _logger = logger;
    }

    /// <summary>
    /// 获取Browser实例，如果尚未初始化则进行初始化
    /// </summary>
    public async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser?.IsConnected == true)
        {
            return _browser;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_browser?.IsConnected == true)
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
        ObjectDisposedException.ThrowIf(_disposed, nameof(PlaywrightService));

        var actualTimeout = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        await _pageLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(PlaywrightService));

            var browser = await GetBrowserAsync();

            await using var context = await CreateBrowserContextAsync(browser);
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout((float)actualTimeout.TotalMilliseconds);

            return await action(page);
        }
        finally
        {
            if (!_disposed)
            {
                _pageLock.Release();
            }
        }
    }

    /// <summary>
    /// 执行需要Page的操作（无返回值版本）
    /// </summary>
    public Task ExecuteWithPageAsync(Func<IPage, Task> action, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return ExecuteWithPageAsync(async page =>
        {
            await action(page);
            return true;
        }, timeout, cancellationToken);
    }

    /// <summary>
    /// 创建浏览器上下文并设置资源阻止策略
    /// </summary>
    private async Task<IBrowserContext> CreateBrowserContextAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BypassCSP = true,
            UserAgent = DefaultUserAgent
        });

        await context.RouteAsync("**/*", route =>
        {
            try
            {
                var resourceType = route.Request.ResourceType;
                if (BlockedResourceTypes.Contains(resourceType))
                {
                    return route.AbortAsync();
                }
                else
                {
                    return route.ContinueAsync();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "路由处理失败: {Url}", route.Request.Url);
                return route.ContinueAsync();
            }
        });

        return context;
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
            _logger?.LogInformation("初始化 Playwright");

            _playwright ??= await Playwright.CreateAsync();

            var options = new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = BrowserArgs
            };

            var browserPath = _userSettingService.CurrentSetting.BrowserPath;
            if (!string.IsNullOrWhiteSpace(browserPath) && File.Exists(browserPath))
            {
                options.ExecutablePath = browserPath;
                _logger?.LogInformation("使用自定义浏览器: {Path}", browserPath);
            }
            else
            {
                _logger?.LogInformation("使用内置 Chromium");
                await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]));
            }

            _browser = await _playwright.Chromium.LaunchAsync(options);
            _browser.Disconnected += (_, _) =>
            {
                _logger?.LogWarning("浏览器连接断开");
                _browser = null;
            };

            _logger?.LogInformation("Playwright 初始化完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Playwright 初始化失败");
            await CleanupAsync();
            throw;
        }
    }

    /// <summary>
    /// 清理浏览器资源
    /// </summary>
    private async Task CleanupAsync()
    {
        if (_browser != null)
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "关闭浏览器时出错");
            }
            finally
            {
                _browser = null;
            }
        }

        _playwright?.Dispose();
        _playwright = null;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 第一次检查：无锁快速路径，避免已释放时获取锁的开销
        if (_disposed)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            // 第二次检查：持锁后再次验证，防止多线程竞态条件
            // 场景：多个线程同时通过第一次检查，但只有第一个线程应该执行释放
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 清理浏览器资源
            await CleanupAsync();

            // 在持锁状态下释放 SemaphoreSlim，确保没有其他线程在等待
            _pageLock.Dispose();
        }
        finally
        {
            _initLock.Release();
            _initLock.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}