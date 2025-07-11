using Microsoft.Playwright;
using IBrowser = Microsoft.Playwright.IBrowser;

namespace MarketAssistant.Infrastructure;

/// <summary>
/// Playwright服务，用于管理Playwright和Browser实例
/// </summary>
public class PlaywrightService
{

    private readonly IUserSettingService _userSettingService;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _pageLock = new SemaphoreSlim(5, 5); // 最多5个并发Page
    private bool _isInitializing = false;
    private DateTime _lastHealthCheck = DateTime.MinValue;
    private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(5);

    public PlaywrightService(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
    }

    /// <summary>
    /// 获取Browser实例，如果尚未初始化则进行初始化
    /// </summary>
    /// <returns>Browser实例</returns>
    private async Task<IBrowser> GetBrowserAsync()
    {
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

            IPage? page = null;
            try
            {
                var browser = await GetBrowserAsync();
                page = await browser.NewPageAsync();

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
            catch (Exception) when (attempt < maxRetries)
            {
                // 指数退避
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt - 1) * 1000);
                await Task.Delay(delay, cancellationToken);
            }
            finally
            {
                // 确保Page被释放
                if (page != null)
                {
                    try
                    {
                        await page.CloseAsync();
                    }
                    catch
                    {
                        // 忽略关闭异常
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
            // 创建Playwright实例
            _playwright = await Playwright.CreateAsync();

            // 获取用户设置中的浏览器路径
            string? browserPath = _userSettingService.CurrentSetting.BrowserPath;

            // 创建Browser实例的选项
            var options = new BrowserTypeLaunchOptions
            {
                Headless = true
            };

            // 如果用户配置了浏览器路径，则使用该路径
            if (!string.IsNullOrEmpty(browserPath))
            {
                options.ExecutablePath = browserPath;
                // 使用用户指定的浏览器
                _browser = await LaunchBrowserByPathAsync(browserPath, options);
            }
            else
            {
                // 用户未指定浏览器路径，确保Playwright内置浏览器已安装
                await EnsureBrowserInstalledAsync();
                // 使用Playwright内置的Chromium
                _browser = await _playwright.Chromium.LaunchAsync(options);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"初始化Playwright时发生异常: {ex.Message}");
            throw;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// 确保Playwright浏览器已安装
    /// </summary>
    private async Task EnsureBrowserInstalledAsync()
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();

            // 检查 Chromium
            if (!File.Exists(playwright.Chromium.ExecutablePath))
            {
                Console.WriteLine("Chromium 未安装，正在安装...");
                var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                if (exitCode != 0)
                {
                    throw new PlaywrightException($"Playwright 安装失败，退出代码: {exitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"安装Playwright时发生未知异常: {ex.Message}");
            Console.WriteLine("请尝试手动执行: dotnet tool update --global playwright");
            Console.WriteLine("或设置镜像源: set PLAYWRIGHT_DOWNLOAD_HOST=https://npmmirror.com/mirrors/playwright");
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
            Console.WriteLine("使用Chrome/Chromium浏览器");
            return await _playwright.Chromium.LaunchAsync(options);
        }
        else if (fileName.Contains("firefox") || fileName.Contains("mozilla"))
        {
            Console.WriteLine("使用Firefox浏览器");
            return await _playwright.Firefox.LaunchAsync(options);
        }
        else if (fileName.Contains("msedge") || fileName.Contains("edge"))
        {
            Console.WriteLine("使用Edge浏览器");
            return await _playwright.Chromium.LaunchAsync(options); // Edge基于Chromium
        }
        else if (fileName.Contains("safari") || fileName.Contains("webkit"))
        {
            Console.WriteLine("使用Safari/WebKit浏览器");
            return await _playwright.Webkit.LaunchAsync(options);
        }
        else
        {
            // 默认尝试使用Chromium启动
            Console.WriteLine($"未识别的浏览器类型: {fileName}，尝试使用Chromium启动");
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