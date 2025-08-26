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

    public PlaywrightService(IUserSettingService userSettingService) : this(userSettingService, null)
    {
    }

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
    /// 执行健康检查
    /// </summary>
    private async Task PerformHealthCheckAsync()
    {
        if (_browser == null || !_browser.IsConnected)
        {
            await ForceReinitializeBrowserAsync();
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
            // Browser连接已断开，重新初始化
            await DisposeAsync();
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
            // 创建Playwright实例（失败时尝试安装后重试）
            int createAttempts = 0;
            while (true)
            {
                try
                {
                    _playwright = await Playwright.CreateAsync();
                    break;
                }
                catch (Exception ex)
                {
                    createAttempts++;
                    _logger?.LogWarning(ex, "创建 Playwright 失败（第 {Attempt} 次），尝试执行安装后重试", createAttempts);
                    if (createAttempts >= 2)
                    {
                        throw;
                    }
                    await TryInstallPlaywrightRuntimeAsync();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            // 获取用户设置中的浏览器路径
            string? browserPath = _userSettingService.CurrentSetting.BrowserPath;

            // 创建Browser实例的选项
            var options = new BrowserTypeLaunchOptions
            {
                Headless = true
            };

            // 启动策略：优先通道，其次显式路径，最后回退到内置Chromium
            if (!string.IsNullOrWhiteSpace(browserPath))
            {
                _browser = await TryLaunchByPreferredStrategyAsync(browserPath!, options);
            }
            else
            {
                // 用户未指定浏览器路径，确保Playwright内置浏览器已安装
                await EnsureBrowserInstalledAsync();
                _browser = await _playwright.Chromium.LaunchAsync(options);
            }

            // 监听断连，便于后续自愈
            if (_browser != null)
            {
                _browser.Disconnected += (_, __) =>
                {
                    _logger?.LogWarning("Playwright 浏览器已断开连接");
                    _browser = null;
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化 Playwright 时发生异常: {Message}", ex.Message);
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
        return Task.Run(() =>
        {
            try
            {
                _logger?.LogInformation("尝试安装 Playwright 运行时与 Chromium 浏览器...");
                _ = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "执行 Playwright 安装时出现异常（已忽略）");
            }
        });
    }

    /// <summary>
    /// 优先使用通道启动，失败后再尝试显式路径与内置浏览器
    /// </summary>
    private async Task<IBrowser> TryLaunchByPreferredStrategyAsync(string browserPath, BrowserTypeLaunchOptions baseOptions)
    {
        if (_playwright == null)
        {
            throw new InvalidOperationException("Playwright实例未初始化");
        }

        // 复制一份选项对象，避免副作用
        BrowserTypeLaunchOptions CloneOptions(BrowserTypeLaunchOptions src) => new BrowserTypeLaunchOptions
        {
            Args = src.Args,
            ChromiumSandbox = src.ChromiumSandbox,
            Devtools = src.Devtools,
            DownloadsPath = src.DownloadsPath,
            Env = src.Env,
            ExecutablePath = src.ExecutablePath,
            HandleSIGHUP = src.HandleSIGHUP,
            HandleSIGINT = src.HandleSIGINT,
            HandleSIGTERM = src.HandleSIGTERM,
            Headless = src.Headless,
            IgnoreAllDefaultArgs = src.IgnoreAllDefaultArgs,
            IgnoreDefaultArgs = src.IgnoreDefaultArgs,
            Proxy = src.Proxy,
            SlowMo = src.SlowMo,
            Timeout = src.Timeout,
            TracesDir = src.TracesDir,
            Channel = src.Channel
        };

        string fileName = Path.GetFileName(browserPath).ToLowerInvariant();

        // 1) 通道优先（msedge/chrome/firefox/webkit）
        string? channel = null;
        if (fileName.Contains("msedge") || fileName.Contains("edge")) channel = "msedge";
        else if (fileName.Contains("chrome")) channel = "chrome";
        else if (fileName.Contains("firefox") || fileName.Contains("mozilla")) channel = "firefox";
        else if (fileName.Contains("safari") || fileName.Contains("webkit")) channel = "webkit";

        if (!string.IsNullOrEmpty(channel))
        {
            var channelOptions = CloneOptions(baseOptions);
            channelOptions.Channel = channel;
            channelOptions.ExecutablePath = null; // 通道模式不需要显式路径
            try
            {
                _logger?.LogInformation("尝试通过通道 {Channel} 启动浏览器（Headless={Headless})", channel, channelOptions.Headless);
                if (channel == "firefox")
                {
                    return await _playwright.Firefox.LaunchAsync(channelOptions);
                }
                if (channel == "webkit")
                {
                    return await _playwright.Webkit.LaunchAsync(channelOptions);
                }
                return await _playwright.Chromium.LaunchAsync(channelOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "通过通道 {Channel} 启动失败，准备回退到显式路径", channel);
            }
        }

        // 2) 显式路径（Chromium/Firefox/Webkit）
        try
        {
            var pathOptions = CloneOptions(baseOptions);
            pathOptions.ExecutablePath = browserPath;
            _logger?.LogInformation("尝试通过显式路径启动浏览器: {Path}", browserPath);
            return await LaunchBrowserByPathAsync(browserPath, pathOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "通过显式路径启动失败，准备回退到内置Chromium");
        }

        // 3) 最后回退：内置Chromium
        await EnsureBrowserInstalledAsync();
        _logger?.LogInformation("回退到内置Chromium 启动（Headless={Headless})", baseOptions.Headless);
        return await _playwright.Chromium.LaunchAsync(baseOptions);
    }

    /// <summary>
    /// 确保Playwright浏览器已安装
    /// </summary>
    private async Task EnsureBrowserInstalledAsync()
    {
        try
        {
            _logger?.LogInformation("确保 Chromium 安装...");
            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }));
            if (exitCode != 0)
            {
                throw new PlaywrightException($"Playwright 安装失败，退出代码: {exitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "安装 Playwright 时发生异常: {Message}", ex.Message);
            _logger?.LogInformation("请尝试手动执行: dotnet tool update --global Microsoft.Playwright.CLI");
            _logger?.LogInformation("或设置镜像源: set PLAYWRIGHT_DOWNLOAD_HOST=https://npmmirror.com/mirrors/playwright");
            throw;
        }
    }

    /// <summary>
    /// 根据浏览器路径智能选择并启动正确的浏览器类型
    /// </summary>
    /// <param name="browserPath">浏览器可执行文件路径</param>
    /// <param name="options">浏览器启动选项</param>
    /// <returns>浏览器实例</returns>
    private async Task<IBrowser> LaunchBrowserByPathAsync(string browserPath, BrowserTypeLaunchOptions options)
    {
        if (_playwright == null)
        {
            throw new InvalidOperationException("Playwright实例未初始化");
        }

        // 根据路径判断浏览器类型
        string fileName = Path.GetFileName(browserPath).ToLowerInvariant();

        // 检测浏览器类型
        if (fileName.Contains("chrome") || fileName.Contains("chromium"))
        {
            _logger?.LogInformation("使用 Chrome/Chromium 浏览器（路径）");
            return await _playwright.Chromium.LaunchAsync(options);
        }
        else if (fileName.Contains("firefox") || fileName.Contains("mozilla"))
        {
            _logger?.LogInformation("使用 Firefox 浏览器（路径）");
            return await _playwright.Firefox.LaunchAsync(options);
        }
        else if (fileName.Contains("msedge") || fileName.Contains("edge"))
        {
            _logger?.LogInformation("使用 Edge 浏览器（路径，Chromium 内核）");
            return await _playwright.Chromium.LaunchAsync(options); // Edge基于Chromium
        }
        else if (fileName.Contains("safari") || fileName.Contains("webkit"))
        {
            _logger?.LogInformation("使用 Safari/WebKit 浏览器（路径）");
            return await _playwright.Webkit.LaunchAsync(options);
        }
        else
        {
            // 默认尝试使用Chromium启动
            _logger?.LogWarning("未识别的浏览器类型: {FileName}，尝试使用Chromium启动", fileName);
            return await _playwright.Chromium.LaunchAsync(options);
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

        // 释放信号量
        _initLock.Dispose();
        _pageLock.Dispose();
    }
}