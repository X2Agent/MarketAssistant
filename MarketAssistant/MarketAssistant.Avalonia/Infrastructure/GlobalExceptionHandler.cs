using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure;

/// <summary>
/// 全局异常处理器，提供跨平台的异常处理功能
/// </summary>
public class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private static GlobalExceptionHandler? _instance;
    private static readonly object _lock = new();

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 初始化全局异常处理
    /// </summary>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        if (_instance != null) return;

        lock (_lock)
        {
            if (_instance != null) return;

            var logger = serviceProvider.GetRequiredService<ILogger<GlobalExceptionHandler>>();
            _instance = new GlobalExceptionHandler(logger, serviceProvider);
            _instance.SetupExceptionHandlers();
        }
    }

    /// <summary>
    /// 设置异常处理器
    /// </summary>
    private void SetupExceptionHandlers()
    {
        // 处理未捕获的异常
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // 处理任务异常
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 处理线程异常（仅在调试模式下）
#if DEBUG
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
#endif
    }

    /// <summary>
    /// 处理未捕获的异常
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger.LogCritical(exception, "发生未处理的异常: {Message}", exception.Message);

            // 在主线程上显示错误信息
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await ShowErrorToUserAsync("应用程序遇到严重错误", exception.Message);
            });
        }
    }

    /// <summary>
    /// 处理未观察到的任务异常
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "发生未观察到的任务异常: {Message}", e.Exception.Message);

        // 标记异常已处理，防止应用程序崩溃
        e.SetObserved();

        // 在主线程上显示错误信息
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ShowErrorToUserAsync("后台任务执行失败", e.Exception.GetBaseException().Message);
        });
    }

    /// <summary>
    /// 处理第一次机会异常（仅调试模式）
    /// </summary>
    private void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        // 只记录非常见的异常，避免日志过多
        if (ShouldLogFirstChanceException(e.Exception))
        {
            _logger.LogDebug(e.Exception, "第一次机会异常: {Message}", e.Exception.Message);
        }
    }

    /// <summary>
    /// 判断是否应该记录第一次机会异常
    /// </summary>
    private static bool ShouldLogFirstChanceException(Exception exception)
    {
        // 过滤掉一些常见的、不需要记录的异常
        return exception is not (
            OperationCanceledException or
            TaskCanceledException
        );
    }

    /// <summary>
    /// 向用户显示错误信息
    /// </summary>
    private async Task ShowErrorToUserAsync(string title, string message)
    {
        try
        {
            // 使用MAUI的跨平台对话框
            await Shell.Current?.DisplayAlert(title, message, "确定");
        }
        catch (Exception ex)
        {
            // 如果显示对话框失败，至少记录到日志
            _logger.LogError(ex, "显示错误对话框失败: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// 处理ViewModel中的异常
    /// </summary>
    public static async Task HandleViewModelExceptionAsync(Exception exception, string operation, ILogger? logger = null)
    {
        var message = $"执行操作 '{operation}' 时发生错误: {exception.Message}";

        // 记录异常
        if (logger != null)
        {
            logger.LogError(exception, message);
        }
        else
        {
            _instance?._logger.LogError(exception, message);
        }

        // 在主线程上显示错误信息
        if (_instance != null)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await _instance.ShowErrorToUserAsync("操作失败", exception.Message);
            });
        }
    }

    /// <summary>
    /// 安全执行异步操作，自动处理异常和IsBusy状态
    /// </summary>
    public static async Task SafeExecuteAsync(Func<Task> operation, Action<bool>? setBusy = null, string? operationName = null, ILogger? logger = null)
    {
        setBusy?.Invoke(true);

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            await HandleViewModelExceptionAsync(ex, operationName ?? "未知操作", logger);
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }

    /// <summary>
    /// 安全执行同步操作，自动处理异常和IsBusy状态
    /// </summary>
    public static void SafeExecute(Action operation, Action<bool>? setBusy = null, string? operationName = null, ILogger? logger = null)
    {
        setBusy?.Invoke(true);

        try
        {
            operation();
        }
        catch (Exception ex)
        {
            Task.Run(async () => await HandleViewModelExceptionAsync(ex, operationName ?? "未知操作", logger));
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }

    /// <summary>
    /// 安全执行带返回值的同步操作
    /// </summary>
    public static T? SafeExecute<T>(Func<T> operation, Action<bool>? setBusy = null, string? operationName = null, ILogger? logger = null)
    {
        setBusy?.Invoke(true);

        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            Task.Run(async () => await HandleViewModelExceptionAsync(ex, operationName ?? "未知操作", logger));
            return default;
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }

    /// <summary>
    /// 安全执行带返回值的异步操作
    /// </summary>
    public static async Task<T?> SafeExecuteAsync<T>(Func<Task<T>> operation, Action<bool>? setBusy = null, string? operationName = null, ILogger? logger = null)
    {
        setBusy?.Invoke(true);

        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            await HandleViewModelExceptionAsync(ex, operationName ?? "未知操作", logger);
            return default;
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    public static void Cleanup()
    {
        if (_instance == null) return;

        lock (_lock)
        {
            if (_instance == null) return;

            AppDomain.CurrentDomain.UnhandledException -= _instance.OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= _instance.OnUnobservedTaskException;

#if DEBUG
            AppDomain.CurrentDomain.FirstChanceException -= _instance.OnFirstChanceException;
#endif

            _instance = null;
        }
    }
}