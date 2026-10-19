using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.ViewModels;

public abstract class ViewModelBase(ILogger? logger = null) : ObservableObject
{
    private bool _isBusy;
    private LoadState _state = LoadState.Idle;
    private bool _isInitialLoading;
    private bool _hasLoadedContent;
    private string _errorMessage = string.Empty;
    private string _emptyMessage = "暂无数据";
    private int _loadGeneration;
    protected readonly ILogger? Logger = logger;

    /// <summary>
    /// 当前市场上下文（由 SubscribeToMarketChanges 设置）
    /// </summary>
    protected MarketContext? MarketContext { get; private set; }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnBusyChanged();
            }
        }
    }

    /// <summary>
    /// IsBusy 变化后的回调，供派生类刷新依赖忙碌状态的派生属性。
    /// </summary>
    protected virtual void OnBusyChanged()
    {
    }

    /// <summary>
    /// 当前页面数据加载状态。
    /// </summary>
    public LoadState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
                return;

            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(OverlayState));
            OnPropertyChanged(nameof(OverlayMessage));
        }
    }

    /// <summary>
    /// 仅首次加载为 true（用于骨架屏）。刷新时不置位，避免内容区闪烁。
    /// </summary>
    public bool IsInitialLoading
    {
        get => _isInitialLoading;
        private set => SetProperty(ref _isInitialLoading, value);
    }

    /// <summary>
    /// 是否已成功加载过内容。用于区分「首次加载」与「已有数据时刷新」。
    /// </summary>
    public bool HasLoadedContent
    {
        get => _hasLoadedContent;
        private set
        {
            if (SetProperty(ref _hasLoadedContent, value))
                OnPropertyChanged(nameof(OverlayState));
        }
    }

    /// <summary>
    /// 供状态浮层绑定的展示状态：已有数据时的刷新不显示加载浮层，避免遮挡既有内容。
    /// </summary>
    public LoadState OverlayState => State == LoadState.Loading && HasLoadedContent ? LoadState.Idle : State;

    /// <summary>
    /// 供状态浮层绑定的提示正文：失败态展示错误信息，空态展示空态文案。
    /// </summary>
    public string OverlayMessage => State == LoadState.Failed ? ErrorMessage : EmptyMessage;

    public bool IsLoading => State == LoadState.Loading;

    /// <summary>加载失败（异常或数据源不可用），与「无数据」严格区分。</summary>
    public bool HasError => State == LoadState.Failed;

    public bool IsEmpty => State == LoadState.Empty;

    public bool IsReady => State == LoadState.Ready;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(OverlayMessage));
        }
    }

    /// <summary>空态提示文案，页面可覆盖以给出更具体的说明。</summary>
    public string EmptyMessage
    {
        get => _emptyMessage;
        protected set
        {
            if (SetProperty(ref _emptyMessage, value))
                OnPropertyChanged(nameof(OverlayMessage));
        }
    }

    /// <summary>失败态的重试命令，由页面通过 <see cref="SetRetryCommand"/> 提供。</summary>
    public IAsyncRelayCommand? RetryCommand { get; protected set; }

    public virtual string Title => string.Empty;

    /// <summary>
    /// 注册重试入口，供失败态浮层绑定。
    /// </summary>
    protected void SetRetryCommand(Func<Task> retryAction)
    {
        RetryCommand = new AsyncRelayCommand(retryAction);
    }

    /// <summary>
    /// 执行一次页面级加载，并把结果映射为 <see cref="LoadState"/>。
    /// loader 返回 false 表示数据源可用但无数据（Empty）；抛出异常表示失败（Failed）。
    /// 用户主动取消视为未加载（Idle），不展示错误。
    /// </summary>
    /// <remarks>
    /// 支持并发重入（如详情页切换资产/周期时取消旧加载后立刻重新进入）：
    /// 仅最新一次调用的完成可以写入状态，被取代的过期调用直接丢弃结果，
    /// 避免其延迟返回（取消令牌可能要等请求自然结束才触发）时把新加载已就绪的状态覆写回 Idle/Failed。
    /// </remarks>
    protected async Task LoadAsync(
        Func<CancellationToken, Task<bool>> loader,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var generation = ++_loadGeneration;

        var isFirstLoad = State == LoadState.Idle;
        if (isFirstLoad)
            IsInitialLoading = true;

        State = LoadState.Loading;
        ErrorMessage = string.Empty;

        try
        {
            var hasData = await loader(cancellationToken);
            if (generation != _loadGeneration)
                return;

            if (hasData)
                HasLoadedContent = true;

            State = hasData ? LoadState.Ready : LoadState.Empty;
        }
        catch (OperationCanceledException)
        {
            if (generation != _loadGeneration)
                return;

            Logger?.LogInformation("{OperationName} 已取消", operationName);
            State = LoadState.Idle;
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration)
                return;

            ErrorMessage = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, operationName);
            State = LoadState.Failed;
            Logger?.LogError(ex, "{OperationName} 失败", operationName);
        }
        finally
        {
            if (generation == _loadGeneration)
                IsInitialLoading = false;
        }
    }

    /// <summary>
    /// 显式标记失败态，用于页面自行捕获异常后上报（如多段加载中某一段失败）。
    /// </summary>
    protected void SetFailed(string errorMessage)
    {
        ErrorMessage = errorMessage;
        State = LoadState.Failed;
        IsInitialLoading = false;
    }

    /// <summary>
    /// 显式标记空态。
    /// </summary>
    protected void SetEmpty(string? emptyMessage = null)
    {
        if (!string.IsNullOrEmpty(emptyMessage))
            EmptyMessage = emptyMessage;

        ErrorMessage = string.Empty;
        State = LoadState.Empty;
        IsInitialLoading = false;
    }

    /// <summary>
    /// 显式标记就绪态（如筛选条件变化后重新产生可见数据）。
    /// </summary>
    protected void SetReady()
    {
        ErrorMessage = string.Empty;
        IsInitialLoading = false;
        State = LoadState.Ready;
    }

    /// <summary>
    /// 复位到未加载状态。
    /// </summary>
    protected void ResetLoadState()
    {
        ErrorMessage = string.Empty;
        IsInitialLoading = false;
        HasLoadedContent = false;
        State = LoadState.Idle;
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

    protected void UnsubscribeFromMarketChanges(MarketContext marketContext)
    {
        marketContext.PropertyChanged -= OnMarketContextPropertyChanged;
        MarketContext = null;
    }

    /// <summary>
    /// 取消上一次操作并创建新的 <see cref="CancellationTokenSource"/>。
    /// 仅调用 <c>Cancel()</c> 而不立即 <c>Dispose()</c>，避免在飞异步任务读取旧令牌时抛出 <see cref="ObjectDisposedException"/>。
    /// </summary>
    protected static CancellationToken ResetCancellationToken(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts = new CancellationTokenSource();
        return cts.Token;
    }

    private void OnMarketContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarketContext.CurrentMarket) && MarketContext != null)
        {
            OnMarketChanged(MarketContext.CurrentMarket);
        }
    }
}
