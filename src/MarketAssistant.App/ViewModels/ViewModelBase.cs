using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    protected readonly ILogger? Logger;

    /// <summary>
    /// 当前市场上下文（由 SubscribeToMarketChanges 设置）
    /// </summary>
    protected MarketContext? MarketContext { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the view model is busy performing an operation.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// 页面标题
    /// </summary>
    public virtual string Title => string.Empty;

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

    /// <summary>
    /// 市场切换时的回调方法，派生类可重写以响应市场变化
    /// </summary>
    protected virtual void OnMarketChanged(MarketType newMarket)
    {
        // 默认空实现，派生类可按需重写
    }

    /// <summary>
    /// 订阅市场上下文的 PropertyChanged 事件，在 CurrentMarket 变化时调用 OnMarketChanged
    /// </summary>
    protected void SubscribeToMarketChanges(MarketContext marketContext)
    {
        MarketContext = marketContext;
        marketContext.PropertyChanged += OnMarketContextPropertyChanged;
    }

    /// <summary>
    /// 取消订阅市场上下文的 PropertyChanged 事件
    /// </summary>
    protected void UnsubscribeFromMarketChanges(MarketContext marketContext)
    {
        marketContext.PropertyChanged -= OnMarketContextPropertyChanged;
    }

    private void OnMarketContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarketContext.CurrentMarket) && MarketContext != null)
        {
            OnMarketChanged(MarketContext.CurrentMarket);
        }
    }
}
