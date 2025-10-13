using Avalonia.Threading;
using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 全局异常处理器，提供应用级异常捕获和处理
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IDialogService _dialogService;
    private static GlobalExceptionHandler? _instance;
    private static readonly object _lock = new();

    private GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IDialogService dialogService)
    {
        _logger = logger;
        _dialogService = dialogService;
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
            var dialogService = serviceProvider.GetRequiredService<IDialogService>();
            _instance = new GlobalExceptionHandler(logger, dialogService);
            _instance.RegisterHandlers();
        }
    }

    /// <summary>
    /// 注册全局异常处理器
    /// </summary>
    private void RegisterHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        
        // 注册 Avalonia 的 UI 线程异常处理
        if (Dispatcher.UIThread != null)
        {
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        }

#if DEBUG
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
#endif
    }

    /// <summary>
    /// 处理未捕获的异常
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception) return;

        _logger.LogCritical(exception, "发生未处理的异常 (IsTerminating: {IsTerminating})", e.IsTerminating);

        if (e.IsTerminating)
        {
            WriteCrashLog(exception);
        }
        else
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var message = ErrorMessageMapper.GetUserFriendlyMessage(exception);
                await ShowErrorAsync("应用程序遇到严重错误", message);
            });
        }
    }

    /// <summary>
    /// 处理未观察到的任务异常
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "发生未观察到的任务异常");
        e.SetObserved();

        Dispatcher.UIThread.Post(async () =>
        {
            var message = ErrorMessageMapper.GetUserFriendlyMessage(e.Exception.GetBaseException());
            await ShowErrorAsync("后台任务执行失败", message);
        });
    }

    /// <summary>
    /// 处理 Avalonia Dispatcher 的未捕获异常
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "UI 线程发生未处理的异常");
        
        var message = ErrorMessageMapper.GetUserFriendlyMessage(e.Exception);
        
        // 标记异常已处理，防止应用崩溃
        e.Handled = true;
        
        // 显示错误对话框
        Dispatcher.UIThread.Post(async () =>
        {
            await ShowErrorAsync("操作失败", message);
        });
    }

    /// <summary>
    /// 处理第一次机会异常（仅调试模式）
    /// </summary>
    private void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        if (e.Exception is not (OperationCanceledException or TaskCanceledException))
        {
            _logger.LogDebug(e.Exception, "第一次机会异常: {Message}", e.Exception.Message);
        }
    }

    /// <summary>
    /// 写入崩溃日志
    /// </summary>
    private void WriteCrashLog(Exception exception)
    {
        try
        {
            var crashLogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarketAssistant");
            Directory.CreateDirectory(crashLogDir);

            var crashLogPath = Path.Combine(crashLogDir, "crash.log");
            var crashInfo = $"""
                ========================================
                崩溃时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                ========================================
                异常类型: {exception.GetType().FullName}
                异常消息: {exception.Message}
                堆栈跟踪:
                {exception.StackTrace}
                
                内部异常:
                {exception.InnerException}
                ========================================
                
                
                """;

            File.AppendAllText(crashLogPath, crashInfo);
            _logger.LogInformation("崩溃日志已写入: {CrashLogPath}", crashLogPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写入崩溃日志失败");
        }
    }

    /// <summary>
    /// 向用户显示错误信息
    /// </summary>
    private async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            await _dialogService.ShowMessageAsync(title, message, "知道了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示错误对话框失败");
        }
    }

    /// <summary>
    /// 安全执行异步操作，自动处理异常和IsBusy状态
    /// </summary>
    public static async Task SafeExecuteAsync(
        Func<Task> operation,
        Action<bool>? setBusy = null,
        string? operationName = null,
        ILogger? logger = null)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("GlobalExceptionHandler 未初始化");
        }

        setBusy?.Invoke(true);

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            var message = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, operationName ?? "操作");
            (logger ?? _instance._logger).LogError(ex, "执行 '{Operation}' 时发生错误", operationName ?? "未知操作");

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await _instance.ShowErrorAsync("操作失败", message);
            });
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }

    /// <summary>
    /// 安全执行带返回值的异步操作
    /// </summary>
    public static async Task<T?> SafeExecuteAsync<T>(
        Func<Task<T>> operation,
        Action<bool>? setBusy = null,
        string? operationName = null,
        ILogger? logger = null)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("GlobalExceptionHandler 未初始化");
        }

        setBusy?.Invoke(true);

        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            var message = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, operationName ?? "操作");
            (logger ?? _instance._logger).LogError(ex, "执行 '{Operation}' 时发生错误", operationName ?? "未知操作");

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await _instance.ShowErrorAsync("操作失败", message);
            });

            return default;
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }

    /// <summary>
    /// 安全执行同步操作，自动处理异常和IsBusy状态
    /// </summary>
    public static void SafeExecute(
        Action operation,
        Action<bool>? setBusy = null,
        string? operationName = null,
        ILogger? logger = null)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("GlobalExceptionHandler 未初始化");
        }

        setBusy?.Invoke(true);

        try
        {
            operation();
        }
        catch (Exception ex)
        {
            var message = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, operationName ?? "操作");
            (logger ?? _instance._logger).LogError(ex, "执行 '{Operation}' 时发生错误", operationName ?? "未知操作");

            // 使用 Post 而不是 InvokeAsync，避免阻塞当前线程
            Dispatcher.UIThread.Post(async () =>
            {
                await _instance.ShowErrorAsync("操作失败", message);
            });
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
            
            if (Dispatcher.UIThread != null)
            {
                Dispatcher.UIThread.UnhandledException -= _instance.OnDispatcherUnhandledException;
            }

#if DEBUG
            AppDomain.CurrentDomain.FirstChanceException -= _instance.OnFirstChanceException;
#endif

            _instance = null;
        }
    }
}
