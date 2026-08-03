using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 全局异常处理器，提供应用级异常捕获和处理。
/// 通过 DI 注册为 Singleton，由 <see cref="Initialize"/> 激活事件钩子。
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IDialogService _dialogService;
    private static GlobalExceptionHandler? _instance;
    private static readonly object _lock = new();

    // 通过 DI 注入具体服务
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IDialogService dialogService)
    {
        _logger = logger;
        _dialogService = dialogService;
    }

    /// <summary>
    /// 注册全局异常处理钩子。应在 DI 容器构建完毕后调用一次，
    /// 传入由 DI 容器创建的实例，避免静态工厂自行 new。
    /// </summary>
    public static void Initialize(GlobalExceptionHandler instance)
    {
        if (_instance != null) return;

        lock (_lock)
        {
            if (_instance != null) return;
            _instance = instance;
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
        // OutOfMemoryException / StackOverflowException 之类的致命错误不应被吞并，让进程终止并写入崩溃日志
        if (IsFatalException(e.Exception))
        {
            _logger.LogCritical(e.Exception, "UI 线程发生致命异常，不处理，允许进程终止");
            WriteCrashLog(e.Exception);
            return; // e.Handled 保持默认 false，进程终止
        }

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
    /// 判断是否是不可恢复的致命异常
    /// </summary>
    private static bool IsFatalException(Exception ex) =>
        ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException;

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
        setBusy?.Invoke(true);

        try
        {
            await operation();
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            logger?.LogInformation("'{Operation}' 被用户取消", operationName ?? "未知操作");
        }
        catch (Exception ex)
        {
            if (_instance != null)
            {
                var message = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, operationName ?? "操作");
                (logger ?? _instance._logger).LogError(ex, "执行 '{Operation}' 时发生错误", operationName ?? "未知操作");

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await _instance.ShowErrorAsync("操作失败", message);
                });
            }
            else
            {
                // 处理器尚未初始化（应用启动早期）：将异常传递给上层调用程序
                logger?.LogError(ex, "执行 '{Operation}' 时发生错误（全局处理器未就绪）", operationName ?? "未知操作");
                throw;
            }
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
        setBusy?.Invoke(true);

        try
        {
            return await operation();
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            logger?.LogInformation("'{Operation}' 被用户取消", operationName ?? "未知操作");
            return default;
        }
        catch (Exception ex)
        {
            if (_instance != null)
            {
                var message = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, operationName ?? "操作");
                (logger ?? _instance._logger).LogError(ex, "执行 '{Operation}' 时发生错误", operationName ?? "未知操作");

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await _instance.ShowErrorAsync("操作失败", message);
                });

                return default;
            }
            else
            {
                logger?.LogError(ex, "执行 '{Operation}' 时发生错误（全局处理器未就绪）", operationName ?? "未知操作");
                throw;
            }
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
        setBusy?.Invoke(true);

        try
        {
            operation();
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            logger?.LogInformation("'{Operation}' 被用户取消", operationName ?? "未知操作");
        }
        catch (Exception ex)
        {
            if (_instance != null)
            {
                var message = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, operationName ?? "操作");
                (logger ?? _instance._logger).LogError(ex, "执行 '{Operation}' 时发生错误", operationName ?? "未知操作");

                Dispatcher.UIThread.Post(async () =>
                {
                    await _instance.ShowErrorAsync("操作失败", message);
                });
            }
            else
            {
                logger?.LogError(ex, "执行 '{Operation}' 时发生错误（全局处理器未就绪）", operationName ?? "未知操作");
                throw;
            }
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

            _instance = null;
        }
    }
}
