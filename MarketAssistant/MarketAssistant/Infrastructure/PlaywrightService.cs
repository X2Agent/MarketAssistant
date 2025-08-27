using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using IBrowser = Microsoft.Playwright.IBrowser;

namespace MarketAssistant.Infrastructure;

/// <summary>
/// Playwright服务，用于管理Playwright和Browser实例
/// </summary>
public class PlaywrightService
{

    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<PlaywrightService>? _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _pageLock = new SemaphoreSlim(5, 5); // 最多5个并发Page
    private bool _isInitializing = false;
    private DateTime _lastHealthCheck = DateTime.MinValue;
    private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(5);
    private const string _defaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

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

            await InitializePlaywrightAsync();
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
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">需要执行的操作</param>
    /// <param name="timeout">操作超时时间，默认30秒</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteWithPageAsync<T>(Func<IPage, Task<T>> action, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // 检查是否已取消
            cancellationToken.ThrowIfCancellationRequested();

            // 等待获取Page槽位
            await _pageLock.WaitAsync(cancellationToken);

            IBrowserContext? context = null;
            IPage? page = null;
            try
            {
                var browser = await GetBrowserAsync();
                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    BypassCSP = true,
                    UserAgent = _defaultUserAgent
                });

                // 轻量请求策略：阻止图片/媒体/字体，降低崩溃概率与资源开销
                await context.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        var rt = route.Request.ResourceType;
                        if (rt == "image" || rt == "media" || rt == "font")
                        {
                            await route.AbortAsync();
                        }
                        else
                        {
                            await route.ContinueAsync();
                        }
                    }
                    catch
                    {
                        // 在路由回调中忽略异常，避免影响主流程
                        try { await route.ContinueAsync(); } catch { }
                    }
                });
                page = await context.NewPageAsync();

                // 设置页面超时
                page.SetDefaultTimeout((float)timeout.Value.TotalMilliseconds);

                // 执行用户操作
                var result = await action(page);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PlaywrightException ex) when (attempt < maxRetries)
            {
                _logger?.LogWarning(ex, "Playwright 异常，准备重建浏览器后重试，第 {Attempt} 次", attempt);
                await ForceReinitializeBrowserAsync();
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt - 1) * 1000);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger?.LogWarning(ex, "执行页面操作失败，准备重试，第 {Attempt} 次", attempt);
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt - 1) * 1000);
                await Task.Delay(delay, cancellationToken);
            }
            finally
            {
                // 确保Page/Context被释放
                if (page != null)
                {
                    try
                    {
                        await page.CloseAsync();
                    }
                    catch
                    {
                    }
                }

                if (context != null)
                {
                    try
                    {
                        await context.CloseAsync();
                    }
                    catch
                    {
                    }
                }

                // 释放Page槽位
                _pageLock.Release();
            }
        }

        // 所有重试都失败了
        throw new InvalidOperationException($"Playwright 操作在 {maxRetries} 次重试后仍然失败");
    }

    /// <summary>
    /// 执行需要Page的操作（无返回值版本）
    /// </summary>
    /// <param name="action">需要执行的操作</param>
    /// <param name="timeout">操作超时时间，默认30秒</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ExecuteWithPageAsync(Func<IPage, Task> action, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await ExecuteWithPageAsync(async page =>
        {
            await action(page);
            return true; // 返回一个占位值
        }, timeout, cancellationToken);
    }

    /// <summary>
    /// 强制重新初始化浏览器
    /// </summary>
    private async Task ForceReinitializeBrowserAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            // 关闭现有浏览器
            if (_browser != null)
            {
                try
                {
                    await _browser.CloseAsync();
                }
                catch
                {
                    // 忽略关闭异常
                }
                _browser = null;
            }

            // 重新初始化
            await InitializePlaywrightAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 执行健康检查（如果需要）
    /// </summary>
    private async Task PerformHealthCheckIfNeeded()
    {
        var now = DateTime.UtcNow;
        if (now - _lastHealthCheck < _healthCheckInterval)
        {
            return; // 还未到检查时间
        }

        _lastHealthCheck = now;

        // 检查Browser连接状态
        if (_browser != null && !_browser.IsConnected)
        {
            // Browser连接已断开，只关闭浏览器，避免处置信号量
            await CloseBrowserAsync();
        }
    }

    /// <summary>
    /// 初始化Playwright和Browser实例
    /// </summary>
    private async Task InitializePlaywrightAsync()
    {
        if (_isInitializing || _browser != null)
        {
            return;
        }

        _isInitializing = true;

        try
        {
            _logger?.LogInformation("初始化 Playwright...");

            // 配置环境变量
            ConfigurePlaywrightEnvironment(false);

            // 创建Playwright实例
            _playwright = await Playwright.CreateAsync();

            // 获取用户设置中的浏览器路径
            string? browserPath = _userSettingService.CurrentSetting.BrowserPath;

            // 创建浏览器启动选项
            var options = new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--disable-gpu",
                    "--disable-extensions",
                    "--disable-dev-shm-usage",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-background-networking",
                    "--disable-background-timer-throttling"
                }
            };

            // 如果指定了浏览器路径，使用该路径
            if (!string.IsNullOrWhiteSpace(browserPath) && File.Exists(browserPath))
            {
                options.ExecutablePath = browserPath;
                _logger?.LogInformation("使用自定义浏览器路径: {Path}", browserPath);
            }
            else
            {
                // 没有路径或路径不存在，使用内置Chromium
                _logger?.LogInformation("使用内置 Chromium");
                await Task.Run(() => Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }));
            }

            // 启动浏览器
            _browser = await _playwright.Chromium.LaunchAsync(options);

            // 监听断连事件
            if (_browser != null)
            {
                _browser.Disconnected += (_, __) =>
                {
                    _logger?.LogWarning("浏览器连接断开");
                    _browser = null;
                };
            }

            _logger?.LogInformation("Playwright 初始化完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Playwright 初始化失败: {Message}", ex.Message);
            
            // 清理失败状态
            _playwright?.Dispose();
            _playwright = null;
            _browser = null;
            
            throw;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// 安装 Playwright 运行时与浏览器（容错执行，不抛异常）
    /// </summary>
    private Task TryInstallPlaywrightRuntimeAsync()
    {
        return TryInstallPlaywrightRuntimeAsync(false);
    }

    private Task TryInstallPlaywrightRuntimeAsync(bool useMirror)
    {
        return Task.Run(() =>
        {
            try
            {
                ConfigurePlaywrightEnvironment(useMirror);
                _logger?.LogInformation("安装 Playwright 运行时与 Chromium 浏览器（mirror={Mirror}）...", useMirror);
                var exit = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                if (exit != 0)
                {
                    _logger?.LogWarning("Playwright 安装返回非零退出码: {Exit}", exit);
                    CleanupPlaywrightCacheSafe();
                    exit = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                    if (exit != 0)
                    {
                        _logger?.LogWarning("Playwright 安装在清理缓存后仍返回非零退出码: {Exit}", exit);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "执行 Playwright 安装时出现异常（已忽略）");
            }
        });
    }

    private void ConfigurePlaywrightEnvironment(bool useMirror)
    {
        try
        {
            var browsersPath = GetBrowsersPath();
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browsersPath, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("PLAYWRIGHT_CLI_LOG", "1", EnvironmentVariableTarget.Process);
            if (useMirror)
            {
                Environment.SetEnvironmentVariable("PLAYWRIGHT_DOWNLOAD_HOST", "https://npmmirror.com/mirrors/playwright", EnvironmentVariableTarget.Process);
            }
        }
        catch
        {
        }
    }

    private static string GetBrowsersPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "ms-playwright");
    }

    private void CleanupPlaywrightCacheSafe()
    {
        try
        {
            var path = GetBrowsersPath();
            if (Directory.Exists(path))
            {
                _logger?.LogWarning("清理本地 Playwright 浏览器缓存: {Path}", path);
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "清理 Playwright 缓存目录时出现异常（已忽略）");
        }
    }







    /// <summary>
    /// 优雅关闭服务
    /// </summary>
    /// <param name="timeout">等待正在进行的操作完成的超时时间</param>
    public async Task GracefulShutdownAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        const int maxConcurrentPages = 5;

        // 等待所有正在进行的操作完成
        var waitStart = DateTime.Now;
        while (_pageLock.CurrentCount < maxConcurrentPages &&
               DateTime.Now - waitStart < timeout)
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

        // 释放信号量（仅应用退出时调用）
        _initLock.Dispose();
        _pageLock.Dispose();
    }

    /// <summary>
    /// 仅关闭浏览器与 Playwright（不释放信号量），用于健康检查/自愈
    /// </summary>
    private async Task CloseBrowserAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            // 关闭浏览器
            if (_browser != null)
            {
                try
                {
                    await _browser.CloseAsync();
                }
                catch
                {
                    // 忽略关闭异常
                }
                _browser = null;
            }

            // 释放 Playwright
            _playwright?.Dispose();
            _playwright = null;
        }
        finally
        {
            _initLock.Release();
        }
    }
}