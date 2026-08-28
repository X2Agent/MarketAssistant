using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MarketAssistant.Applications.Crypto;
using MarketAssistant.DataProviders;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 后台市场监控器，订阅实时价格并根据策略触发交易。
/// 使用 Channel 缓冲价格更新，按顺序处理每个 tick。
/// </summary>
public class MarketMonitor : IDisposable
{
    private readonly BinanceWebSocketService _webSocketService;
    private readonly BinanceUserDataStreamService _userDataStreamService;
    private readonly StrategyEngine _strategyEngine;
    private readonly TradeExecutor _tradeExecutor;
    private readonly AISignalStrategyExecutor _aiSignalExecutor;
    private readonly OrderStateSyncService _orderStateSyncService;
    private readonly TradingStrategyService _strategyService;
    private readonly ILogger<MarketMonitor> _logger;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private Task? _consumerTask;

    private readonly Channel<(string Symbol, decimal Price)> _priceChannel =
        Channel.CreateBounded<(string, decimal)>(new BoundedChannelOptions(1000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _strategyLocks = new();

    /// <summary>
    /// 策略执行失败的冷却期。止损/止盈/追踪止损/网格/DCA 等策略触发评估是纯价格/状态比较，
    /// 若执行失败（网络异常、风控拒绝、超时等）且不记录任何状态，会在每个价格 tick 重复触发重试
    /// （miniTicker 约每秒一次），形成请求风暴。失败后冷却一段时间，把重试频率限制到可控范围。
    /// </summary>
    private static readonly TimeSpan StrategyFailureCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 网络类失败的短冷却：网络异常属临时状态，快速重试以免错过触发窗口
    /// （止损单挂 30 秒的冷却可能已造成实际损失）。
    /// </summary>
    private static readonly TimeSpan NetworkFailureCooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 各策略最近一次执行失败的冷却截止时间（UTC）
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _strategyFailureCooldowns = new();

    /// <summary>
    /// 已取消的 Token，用于 _cts 为 null 时（未启动或已释放）确保异步操作立即返回而非不可取消地继续执行。
    /// 使用 CancellationToken.None 会导致操作不可取消，在 StopAsync/Dispose 后仍可能执行交易。
    /// </summary>
    private static readonly CancellationToken StoppedToken = new(canceled: true);

    /// <summary>
    /// 获取当前可用的 CancellationToken：运行中返回 _cts.Token，未运行或已释放返回已取消的 Token。
    /// </summary>
    private CancellationToken MonitorToken => _cts?.Token ?? StoppedToken;

    /// <summary>
    /// 正在执行的策略任务列表，用于 StopAsync 时等待全部完成，避免订单已发送但本地状态未更新
    /// </summary>
    private readonly List<Task> _pendingStrategyTasks = [];
    private readonly object _pendingTasksLock = new();

    // 适配 BinanceWebSocketService.PriceUpdated 事件签名（Action<string, decimal, decimal>）
    // 到不含 changePercent 的 OnPriceUpdated 处理方法，需存储委托实例以支持 -= 取消订阅
    private readonly Action<string, decimal, decimal> _priceUpdatedAdapter;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// 当交易执行时触发的事件（UI 可订阅来刷新）
    /// </summary>
    public event Action<TradeRecord>? TradeExecuted;

    /// <summary>
    /// 当监控状态变化时触发
    /// </summary>
    public event Action<bool>? StatusChanged;

    public MarketMonitor(
        BinanceWebSocketService webSocketService,
        StrategyEngine strategyEngine,
        TradeExecutor tradeExecutor,
        AISignalStrategyExecutor aiSignalExecutor,
        OrderStateSyncService orderStateSyncService,
        TradingStrategyService strategyService,
        BinanceUserDataStreamService userDataStreamService,
        ILogger<MarketMonitor> logger)
    {
        _webSocketService = webSocketService;
        _strategyEngine = strategyEngine;
        _tradeExecutor = tradeExecutor;
        _aiSignalExecutor = aiSignalExecutor;
        _orderStateSyncService = orderStateSyncService;
        _strategyService = strategyService;
        _userDataStreamService = userDataStreamService;
        _logger = logger;
        _priceUpdatedAdapter = (symbol, lastPrice, _) => OnPriceUpdated(symbol, lastPrice);
        _strategyService.StrategiesChanged += OnStrategiesChanged;
    }

    /// <summary>
    /// 启动后台监控
    /// </summary>
    public async Task StartAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_isRunning)
                return;

            _cts = new CancellationTokenSource();
            _isRunning = true;

            var activeStrategies = await _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active);
            var instrumentSymbols = activeStrategies
                .Select(s => s.Symbol.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (instrumentSymbols.Count > 0)
                await _webSocketService.SubscribeAsync(WebSocketSubscriberKeys.MarketMonitor, instrumentSymbols);

            _webSocketService.PriceUpdated += _priceUpdatedAdapter;
            _consumerTask = Task.Run(() => ConsumePriceUpdatesAsync(_cts.Token));

            _userDataStreamService.OrderUpdate += OnOrderUpdate;
            await _userDataStreamService.StartAsync();

            _logger.LogInformation("MarketMonitor 已启动，监控 {Count} 个交易标的", instrumentSymbols.Count);
            StatusChanged?.Invoke(true);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 停止后台监控
    /// </summary>
    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (!_isRunning)
                return;

            _webSocketService.PriceUpdated -= _priceUpdatedAdapter;
            _userDataStreamService.OrderUpdate -= OnOrderUpdate;
            await _userDataStreamService.StopAsync();
            _cts?.Cancel();

            if (_consumerTask != null)
            {
                try { await _consumerTask; }
                catch (OperationCanceledException) { }
            }

            // 等待所有正在执行的策略任务完成，避免订单已发送但本地状态未更新
            Task[] pendingTasks;
            lock (_pendingTasksLock)
                pendingTasks = _pendingStrategyTasks.ToArray();

            if (pendingTasks.Length > 0)
            {
                _logger.LogInformation("等待 {Count} 个策略任务完成...", pendingTasks.Length);
                try
                {
                    await Task.WhenAll(pendingTasks).WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "等待策略任务完成时超时或出错，部分状态可能未持久化");
                }
            }

            await _webSocketService.UnsubscribeAllAsync(WebSocketSubscriberKeys.MarketMonitor);

            _isRunning = false;
            _logger.LogInformation("MarketMonitor 已停止");
            StatusChanged?.Invoke(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 刷新监控的交易标的列表（策略增减后调用）
    /// </summary>
    public async Task RefreshSubscriptionsAsync()
    {
        if (!_isRunning)
            return;

        var activeStrategies = await _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active);
        var newSymbols = activeStrategies
            .Select(s => s.Symbol.ToLowerInvariant())
            .Distinct()
            .ToList();

        // 以完整集合替换监控方的订阅；不再先整体清空，避免误伤其他模块的订阅
        await _webSocketService.SubscribeAsync(WebSocketSubscriberKeys.MarketMonitor, newSymbols);

        _logger.LogInformation("已刷新监控列表: {Count} 个交易标的", newSymbols.Count);
    }

    private void OnPriceUpdated(string symbol, decimal lastPrice)
    {
        _priceChannel.Writer.TryWrite((symbol, lastPrice));
    }

    /// <summary>
    /// 用户数据流订单回报：仅终态/部分成交触发同步，复用 OrderStateSyncService 现有对账逻辑。
    /// fire-and-forget 避免阻塞 WS 接收循环。
    /// </summary>
    private void OnOrderUpdate(ExecutionReport report)
    {
        // 仅终态/部分成交需触发同步；NEW/TRADE 中间态由现有轮询覆盖
        if (report.OrderStatus is not ("FILLED" or "PARTIALLY_FILLED" or "CANCELED"))
            return;

        _ = OnOrderUpdateAsync(report);
    }

    private async Task OnOrderUpdateAsync(ExecutionReport report)
    {
        try
        {
            await _orderStateSyncService.SyncPendingOrdersAsync(
                report.Symbol, force: true, ct: MonitorToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "用户数据流触发订单同步失败: {Symbol} {ClientOrderId}",
                report.Symbol, report.ClientOrderId);
        }
    }

    /// <summary>
    /// Channel 消费者：顺序评估策略，异步执行触发的交易（每策略独立锁）
    /// </summary>
    private async Task ConsumePriceUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (symbol, price) in _priceChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await _orderStateSyncService.SyncPendingOrdersAsync(symbol, ct: ct);
                    var triggered = await _strategyEngine.EvaluateAndUpdateStrategiesAsync(symbol, price, ct);
                    foreach (var strategy in triggered)
                    {
                        // 执行失败后处于冷却期的策略跳过，防止每 tick 重复触发请求风暴
                        if (IsInFailureCooldown(strategy.Id))
                            continue;

                        var task = ExecuteWithStrategyLockAsync(strategy, price, ct);
                        lock (_pendingTasksLock)
                            _pendingStrategyTasks.Add(task);
                        // 任务完成后自动从列表移除，避免无限增长
                        _ = task.ContinueWith(t =>
                        {
                            lock (_pendingTasksLock)
                                _pendingStrategyTasks.Remove(t);
                        }, TaskScheduler.Default);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "价格更新处理异常: {Symbol}", symbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("价格消费者已取消");
        }
    }

    private void OnStrategiesChanged(object? sender, EventArgs e)
    {
        if (!_isRunning)
            return;

        _ = RefreshSubscriptionsOnChangeAsync();
    }

    private async Task RefreshSubscriptionsOnChangeAsync()
    {
        try
        {
            await RefreshSubscriptionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "策略集合变化后刷新订阅失败");
        }
    }

    /// <summary>
    /// 带策略级锁的异步执行，防止同一策略并发触发
    /// </summary>
    private async Task ExecuteWithStrategyLockAsync(
        TradingStrategy strategy, decimal price, CancellationToken ct)
    {
        var strategyLock = _strategyLocks.GetOrAdd(strategy.Id, _ => new SemaphoreSlim(1, 1));
        if (!await strategyLock.WaitAsync(0, ct))
        {
            _logger.LogDebug("策略 {Id} 正在执行中，跳过本次触发", strategy.Id);
            return;
        }

        try
        {
            await HandleTriggeredStrategyAsync(strategy, price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "策略执行异常: {StrategyId}", strategy.Id);
            // 未分类异常（未走到 TradeResult 分类）按常规冷却处理
            RecordStrategyFailureCooldown(strategy.Id, StrategyFailureCooldown);
        }
        finally
        {
            strategyLock.Release();
        }
    }

    /// <summary>
    /// 记录策略执行失败的冷却截止时间
    /// </summary>
    private void RecordStrategyFailureCooldown(string strategyId, TimeSpan cooldown)
        => _strategyFailureCooldowns[strategyId] = DateTime.UtcNow.Add(cooldown);

    /// <summary>
    /// 判断策略是否处于执行失败冷却期内；冷却期结束后自动清理记录
    /// </summary>
    private bool IsInFailureCooldown(string strategyId)
    {
        if (!_strategyFailureCooldowns.TryGetValue(strategyId, out var until))
            return false;

        if (DateTime.UtcNow >= until)
        {
            _strategyFailureCooldowns.TryRemove(strategyId, out _);
            return false;
        }

        _logger.LogDebug("策略 {StrategyId} 处于失败冷却中，跳过本次触发", strategyId);
        return true;
    }

    private async Task HandleTriggeredStrategyAsync(TradingStrategy strategy, decimal currentPrice)
    {
        if (strategy.Type == StrategyType.AISignal)
        {
            var result = await _aiSignalExecutor.ExecuteAsync(strategy, currentPrice, MonitorToken);
            if (result.TradeExecuted && result.Record != null)
                TradeExecuted?.Invoke(result.Record);

            // HOLD/无持仓完结是正常路径：不进失败冷却，重试频率由
            // StrategyEngine 的 LastTriggeredAt（analysisInterval）节流
            if (result.Outcome != AISignalOutcome.NoTrade)
                await ApplyTradeFailurePolicy(strategy, result.TradeExecuted, result.TradeResult);

            await CheckStrategyCompletionAsync(strategy);
        }
        else
        {
            // 网格交易：交易成功后原子地持久化更新后的网格参数，防止计数和参数不一致
            var pendingCustomParams = strategy.Type == StrategyType.GridTrading ? strategy.CustomParams : null;
            var result = await _tradeExecutor.ExecuteTradeAsync(
                strategy, currentPrice,
                pendingCustomParams: pendingCustomParams,
                requireClose: IsExitOnlyStrategy(strategy, currentPrice),
                ct: MonitorToken);

            if (result.Success && result.Record != null)
                TradeExecuted?.Invoke(result.Record);

            await ApplyTradeFailurePolicy(strategy, result.Success, result);

            await CheckStrategyCompletionAsync(strategy);
        }
    }

    /// <summary>
    /// 按失败类别应用后续策略：
    /// - 成功：清除冷却记录；
    /// - 拒绝类（风控/人工确认被拒/持仓校验不通过）：重试不会改变结果，暂停策略并通知用户，防永久空转；
    /// - 网络类：短冷却后快速重试，避免错过止损/止盈窗口；
    /// - 其他：常规 30s 冷却。
    /// </summary>
    private async Task ApplyTradeFailurePolicy(TradingStrategy strategy, bool success, TradeResult? tradeResult)
    {
        if (success)
        {
            _strategyFailureCooldowns.TryRemove(strategy.Id, out _);
            return;
        }

        var category = tradeResult?.FailureCategory ?? TradeFailureCategory.Other;
        if (category == TradeFailureCategory.Rejected)
        {
            _strategyFailureCooldowns.TryRemove(strategy.Id, out _);
            await PauseStrategyAfterRejectionAsync(strategy, tradeResult);
            return;
        }

        RecordStrategyFailureCooldown(
            strategy.Id,
            category == TradeFailureCategory.Network ? NetworkFailureCooldown : StrategyFailureCooldown);
    }

    /// <summary>
    /// 拒绝类失败后暂停策略：状态置为 Paused（用户可在策略页重新启用），
    /// 并弹通知让用户知晓（策略静默停摆比反复重试更危险）。
    /// </summary>
    private async Task PauseStrategyAfterRejectionAsync(TradingStrategy strategy, TradeResult? tradeResult)
    {
        var reason = tradeResult?.ErrorMessage ?? "交易被拒绝";
        _logger.LogWarning(
            "策略 {StrategyId}（{Type} {Symbol}）触发被拒绝，已自动暂停: {Reason}",
            strategy.Id, strategy.Type, strategy.Symbol, reason);

        try
        {
            await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Paused, MonitorToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "暂停被拒策略失败，策略将保持 Active: {StrategyId}", strategy.Id);
        }
    }

    /// <summary>
    /// 判定策略本次触发是否为"平仓退出"语义：无对应持仓时应在执行器层拒绝下单，
    /// 防止合约模式下退出型触发在持仓已平后反向开出新仓。
    /// 止损/追踪止损为纯退出；止盈仅 Sell 侧为退出（Buy 侧语义为限价建仓）；
    /// 网格仅破网（价格突破网格边界且触及止损/止盈位）为清仓退出，普通网格线买卖仍是开平仓组合。
    /// </summary>
    private static bool IsExitOnlyStrategy(TradingStrategy strategy, decimal currentPrice)
    {
        return strategy.Type switch
        {
            StrategyType.StopLoss => true,
            StrategyType.TakeProfit => strategy.Side == OrderSide.Sell,
            StrategyType.TrailingStop => true,
            StrategyType.GridTrading => IsGridBreakOut(strategy, currentPrice),
            // DCA 评估只会以卖出方向触发出场（止盈/止损清仓），卖出即平仓语义
            StrategyType.DCA => strategy.Side == OrderSide.Sell,
            _ => false
        };
    }

    /// <summary>
    /// 网格破网判定：价格跌破下界且触及破网止损，或涨破上界且触及破网止盈。
    /// </summary>
    private static bool IsGridBreakOut(TradingStrategy strategy, decimal currentPrice)
    {
        if (string.IsNullOrEmpty(strategy.CustomParams))
            return false;

        try
        {
            var gridParams = JsonSerializer.Deserialize<GridTradingParams>(strategy.CustomParams);
            if (gridParams == null || gridParams.GridCount <= 1 || gridParams.UpperPrice <= gridParams.LowerPrice)
                return false;

            if (currentPrice < gridParams.LowerPrice)
                return gridParams.StopLossPrice.HasValue && currentPrice <= gridParams.StopLossPrice.Value;

            if (currentPrice > gridParams.UpperPrice)
                return gridParams.TakeProfitPrice.HasValue && currentPrice >= gridParams.TakeProfitPrice.Value;

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task CheckStrategyCompletionAsync(TradingStrategy strategy)
    {
        if (!strategy.MaxExecutions.HasValue)
            return;

        var updated = await _strategyService.GetStrategyAsync(strategy.Id);
        if (updated != null && updated.ExecutionCount >= updated.MaxExecutions!.Value)
        {
            await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed);
            await _strategyEngine.ClearPeakPriceAsync(strategy.Id);
            _strategyLocks.TryRemove(strategy.Id, out _);
            _strategyFailureCooldowns.TryRemove(strategy.Id, out _);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _priceChannel.Writer.TryComplete();
        foreach (var kvp in _strategyLocks)
            kvp.Value.Dispose();
        _strategyLocks.Clear();
        _strategyFailureCooldowns.Clear();
        _lifecycleLock.Dispose();

        TradeExecuted = null;
        StatusChanged = null;
        _webSocketService.PriceUpdated -= _priceUpdatedAdapter;
        _userDataStreamService.OrderUpdate -= OnOrderUpdate;
        _strategyService.StrategiesChanged -= OnStrategiesChanged;

        GC.SuppressFinalize(this);
    }
}
