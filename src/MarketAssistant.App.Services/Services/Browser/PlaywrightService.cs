using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace MarketAssistant.Services.Browser;

/// <summary>
/// Playwright 浏览器服务，管理浏览器生命周期和并发页面操作。
/// 优先使用系统已安装的 Edge/Chrome，避免下载独立 Chromium。
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
    private string? _cachedBrowserPath;
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
        var currentBrowserPath = _userSettingService.CurrentSetting.BrowserPath;

        // 检查浏览器是否连接且路径未变更
        if (_browser?.IsConnected == true && _cachedBrowserPath == currentBrowserPath)
        {
            return _browser;
        }

        await _initLock.WaitAsync();
        try
        {
            // 双重检查
            if (_browser?.IsConnected == true && _cachedBrowserPath == currentBrowserPath)
            {
                return _browser;
            }

            // 如果路径变更或浏览器未连接，重新初始化
            if (_browser != null)
            {
                await _browser.CloseAsync();
                await _browser.DisposeAsync();
                _browser = null;
            }

            await InitializeBrowserAsync();
            _cachedBrowserPath = currentBrowserPath;
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

            IBrowser browser;
            try
            {
                browser = await GetBrowserAsync();
            }
            catch (FriendlyException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FriendlyException(
                    "浏览器启动失败。请在设置中指定浏览器路径，或确保系统已安装 Chrome/Edge 浏览器。",
                    ex);
            }

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
    /// 初始化 Playwright 并启动浏览器。
    /// BrowserPath 由 BrowserService 自动检测系统 Edge/Chrome 并填入，因此 ExecutablePath 就是系统浏览器。
    /// 策略：1) ExecutablePath（自动检测或用户手动指定）→ 2) 内置 Chromium
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

            var browserPath = _userSettingService.CurrentSetting.BrowserPath;

            // 策略 1：使用 ExecutablePath（BrowserService 自动检测的 Edge/Chrome 或用户手动指定的路径）
            if (!string.IsNullOrWhiteSpace(browserPath) && File.Exists(browserPath))
            {
                try
                {
                    _logger?.LogInformation("使用浏览器: {Path}", browserPath);
                    _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        ExecutablePath = browserPath,
                        Args = BrowserArgs
                    });
                    SetupDisconnectHandler();
                    _logger?.LogInformation("Playwright 已启动: {Path}", browserPath);
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "浏览器启动失败: {Path}，尝试回退到内置 Chromium", browserPath);
                }
            }

            // 策略 2：回退到内置 Chromium（需下载）
            _logger?.LogInformation("无可用的本地浏览器，尝试安装内置 Chromium");
            var installResult = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]));
            if (installResult != 0)
            {
                throw new FriendlyException(
                    $"Chromium 浏览器安装失败（退出码: {installResult}）。" +
                    "请在设置中手动指定 Chrome 或 Edge 浏览器路径，" +
                    "或在终端运行: pwsh bin/Debug/net10.0/playwright.ps1 install");
            }

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = BrowserArgs
            });
            SetupDisconnectHandler();
            _logger?.LogInformation("Playwright 已使用内置 Chromium 启动");
        }
        catch (FriendlyException)
        {
            await CleanupAsync();
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Playwright 初始化失败");
            await CleanupAsync();
            throw new FriendlyException(
                "浏览器初始化失败。请确保系统已安装 Chrome 或 Edge 浏览器，" +
                "或在设置页面手动指定浏览器路径。",
                ex);
        }
    }

    private void SetupDisconnectHandler()
    {
        if (_browser != null)
        {
            _browser.Disconnected += (_, _) =>
            {
                _logger?.LogWarning("浏览器连接断开");
                _browser = null;
            };
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