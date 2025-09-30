using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    protected readonly ILogger? Logger;

    /// <summary>
    /// Gets or sets a value indicating whether the view model is busy performing an operation.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    protected ViewModelBase(ILogger? logger = null)
    {
        Logger = logger;
    }

    /// <summary>
    /// 安全执行异步操作，自动处理异常和IsBusy状态
    /// </summary>
    protected async Task SafeExecuteAsync(Func<Task> operation, string? operationName = null)
    {
        await GlobalExceptionHandler.SafeExecuteAsync(
            operation,
            setBusy: (busy) => IsBusy = busy,
            operationName,
            Logger
        );
    }

    /// <summary>
    /// 安全执行带返回值的异步操作
    /// </summary>
    protected async Task<T?> SafeExecuteAsync<T>(Func<Task<T>> operation, string? operationName = null)
    {
        return await GlobalExceptionHandler.SafeExecuteAsync(
            operation,
            setBusy: (busy) => IsBusy = busy,
            operationName,
            Logger
        );
    }

    /// <summary>
    /// 安全执行同步操作，自动处理异常和IsBusy状态
    /// </summary>
    protected void SafeExecute(Action operation, string? operationName = null)
    {
        GlobalExceptionHandler.SafeExecute(
            operation,
            setBusy: (busy) => IsBusy = busy,
            operationName,
            Logger
        );
    }
}
