using System.Collections.Concurrent;
using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易所侧保护性条件单管理器：为合约模式下的退出型策略（止损/追踪止损/止盈平仓）
/// 维护 reduceOnly 条件单，使"进程退出或网络中断期间止损失效"的限制不再成立。
///
/// 权威原则：条件单挂出后，平仓由交易所触发（权威），客户端 tick 评估降级为兜底——
/// 对账周期内发现挂单缺失则补挂；策略删除/暂停后挂单残留则撤销孤儿单。
///
/// 对账时机：监控启动、策略集合变化（含编辑后参数漂移撤旧挂新）、每 60 秒周期。
/// 挂单本身无成交，成交后由 OrderStateSyncService 既有对账管线回写持仓与统计，
/// 并经 <see cref="OrderStateSyncService.TradeRecordFilled"/> 事件驱动策略完结。
/// 条件单常驻交易所：监控停止/应用退出不撤单，重启后对账自动接管（这正是兜底价值所在）。
/// </summary>
public sealed class ExchangeConditionOrderService
{
    /// <summary>周期对账间隔。覆盖追踪止损激活（峰值首次落库绕过策略变更广播）等旁路状态变化。</summary>
    public static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(60);

    private readonly TradingStrategyService _strategyService;
    private readonly TradeExecutor _tradeExecutor;
    private readonly TradingDataService _dataService;
    private readonly RoutingExchangeClient _exchangeClient;
    private readonly TradingEnvironmentService _environmentService;
    private readonly ILogger<ExchangeConditionOrderService> _logger;

    /// <summary>策略 ID → 在途条件单交易所订单 ID。内存态：重启后由对账重建。</summary>
    private readonly ConcurrentDictionary<string, string> _liveOrderIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _reconcileCts;
    private Task? _reconcileLoopTask;

    public ExchangeConditionOrderService(
        TradingStrategyService strategyService,
        TradeExecutor tradeExecutor,
        TradingDataService dataService,
        RoutingExchangeClient exchangeClient,
        TradingEnvironmentService environmentService,
        ILogger<ExchangeConditionOrderService> logger)
    {
        _strategyService = strategyService;
        _tradeExecutor = tradeExecutor;
        _dataService = dataService;
        _exchangeClient = exchangeClient;
        _environmentService = environmentService;
        _logger = logger;

        // 环境切换后旧环境的订单映射全部失效（不同 API Key 的交易所），由新环境对账重建
        _environmentService.ModeChanged += OnModeChanged;
    }

    /// <summary>
    /// 判定策略是否应由交易所条件单保护（一次性退出语义；止盈 Buy 侧为限价建仓，不在此列）。
    /// </summary>
    public static bool IsProtectiveExitStrategy(TradingStrategy strategy) => strategy.Type switch
    {
        StrategyType.StopLoss => true,
        StrategyType.TrailingStop => true,
        StrategyType.TakeProfit => strategy.Side == OrderSide.Sell,
        _ => false
    };

    /// <summary>
    /// 条件单参数指纹（类型|方向|数量|触发价|回调基点）：策略编辑后与在途挂单比对，
    /// 不一致则撤旧挂新。
    /// </summary>
    internal static string BuildFingerprint(TradingStrategy strategy)
    {
        var trailingDelta = strategy.Type == StrategyType.TrailingStop
            ? TradeExecutor.ResolveTrailingDeltaBasisPoints(strategy).ToString()
            : string.Empty;
        return $"{strategy.Type}|{strategy.Side}|{strategy.Quantity}|{strategy.TriggerPrice}|{trailingDelta}";
    }

    /// <summary>
    /// 指定策略当前是否有在途交易所条件单（客户端触发执行据此跳过，防止双触发）。
    /// </summary>
    public bool HasLiveOrder(string strategyId) => _liveOrderIds.ContainsKey(strategyId);

    /// <summary>策略完结/对账撤销后清理在途映射。</summary>
    public void ClearLiveOrder(string strategyId) => _liveOrderIds.TryRemove(strategyId, out _);

    /// <summary>
    /// 启动周期对账循环并执行首次对账（幂等）。由 MarketMonitor 启动时调用。
    /// </summary>
    public async Task StartAsync()
    {
        lock (_lifecycleLock)
        {
            if (_reconcileLoopTask != null)
                return;

            var cts = new CancellationTokenSource();
            _reconcileCts = cts;
            _reconcileLoopTask = Task.Run(() => ReconcileLoopAsync(cts.Token));
        }

        await ReconcileSafeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 停止周期对账循环。不撤销交易所挂单：条件单常驻交易所，监控停止期间保护依然有效，
    /// 重启后对账自动接管。
    /// </summary>
    public void Stop()
    {
        lock (_lifecycleLock)
        {
            _reconcileCts?.Cancel();
            _reconcileCts = null;
            _reconcileLoopTask = null;
        }
    }

    /// <summary>触发一次对账（吞异常记日志，供事件回调 fire-and-forget 调用）。</summary>
    public async Task ReconcileSafeAsync()
    {
        try
        {
            await ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "交易所条件单对账失败，将在下个周期重试");
        }
    }

    /// <summary>
    /// 对账：撤销孤儿单、补挂缺失单、参数漂移撤旧挂新。
    /// 通过 _reconcileGate 串行化，避免策略变更风暴下并发对账重复挂单。
    /// </summary>
    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (!await _reconcileGate.WaitAsync(0, ct).ConfigureAwait(false))
            return;

        try
        {
            await ReconcileCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task ReconcileCoreAsync(CancellationToken ct)
    {
        var exchangeClient = _exchangeClient.GetActiveClientSnapshot();
        if (!exchangeClient.IsFutures)
        {
            // 现货无条件单语义：清空映射，退出型策略由客户端评估兜底
            _liveOrderIds.Clear();
            return;
        }

        var activeStrategies = await _strategyService
            .GetStrategiesByStatusAsync(StrategyStatus.Active, ct).ConfigureAwait(false);
        var protectiveStrategies = activeStrategies
            .Where(IsProtectiveExitStrategy)
            .ToList();
        var protectiveIds = protectiveStrategies
            .Select(s => s.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var openOrders = await exchangeClient.GetOpenOrdersAsync(null, ct).ConfigureAwait(false);
        var managedOrders = openOrders
            .Where(o => o.ClientOrderId.StartsWith(
                TradeExecutor.ConditionOrderClientOrderIdPrefix, StringComparison.Ordinal))
            .ToList();

        // 1. 撤销孤儿单：挂单仍在交易所，但对应策略已非活跃保护策略（删除/暂停/完结/编辑后类型不再匹配）
        foreach (var order in managedOrders)
        {
            var strategyId = order.ClientOrderId[TradeExecutor.ConditionOrderClientOrderIdPrefix.Length..];
            if (protectiveIds.Contains(strategyId))
                continue;

            try
            {
                await exchangeClient.CancelOrderAsync(order.Symbol, order.OrderId, ct).ConfigureAwait(false);
                _liveOrderIds.TryRemove(strategyId, out _);
                _logger.LogInformation(
                    "已撤销孤儿条件单: {Symbol} {OrderId} (策略 {StrategyId})",
                    order.Symbol, order.OrderId, strategyId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "撤销孤儿条件单失败，下个对账周期重试: {OrderId}", order.OrderId);
            }
        }

        // 2. 补挂缺失单 / 参数漂移撤旧挂新
        foreach (var strategy in protectiveStrategies)
        {
            ct.ThrowIfCancellationRequested();
            await EnsureConditionOrderAsync(exchangeClient, strategy, managedOrders, ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureConditionOrderAsync(
        IExchangeClient exchangeClient,
        TradingStrategy strategy,
        List<ExchangeOrderResult> managedOrders,
        CancellationToken ct)
    {
        var clientOrderId = TradeExecutor.BuildConditionOrderClientOrderId(strategy.Id);
        var existing = managedOrders.FirstOrDefault(o =>
            string.Equals(o.ClientOrderId, clientOrderId, StringComparison.Ordinal));
        var expectedFingerprint = BuildFingerprint(strategy);

        if (existing != null)
        {
            _liveOrderIds[strategy.Id] = existing.OrderId;
            if (string.Equals(strategy.ConditionOrderFingerprint, expectedFingerprint, StringComparison.Ordinal))
                return;

            // 参数漂移（用户编辑策略）：撤旧挂新；撤单失败则留待下个周期，避免出现双单
            try
            {
                await exchangeClient.CancelOrderAsync(existing.Symbol, existing.OrderId, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "策略参数已变化，撤销旧条件单待重挂: {StrategyId} {OrderId}",
                    strategy.Id, existing.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "参数漂移撤单失败，下个对账周期重试: {StrategyId} {OrderId}",
                    strategy.Id, existing.OrderId);
                return;
            }
        }
        else if (strategy.Type == StrategyType.TrailingStop && !strategy.TrailingPeakPrice.HasValue)
        {
            // 交易所追踪单挂出即激活：追踪尚未激活（无峰值记录）时提前挂单会过早止损，
            // 仅保留客户端评估兜底，激活后（峰值首次落库）由周期对账补挂
            _liveOrderIds.TryRemove(strategy.Id, out _);
            return;
        }

        var result = await _tradeExecutor.PlaceConditionOrderAsync(strategy, ct).ConfigureAwait(false);
        if (result.Success && result.Record != null)
        {
            _liveOrderIds[strategy.Id] = result.Record.ExchangeOrderId.ToString();
            await _dataService
                .UpdateStrategyConditionOrderFingerprintAsync(strategy.Id, expectedFingerprint, ct)
                .ConfigureAwait(false);
        }
        else
        {
            // 挂新失败：清在途映射使客户端评估兜底保持激活。参数漂移路径此处旧单已撤，
            // 若保留旧映射会令 HasLiveOrder=true 而跳过客户端执行，产生最长一个对账周期的保护空窗
            _liveOrderIds.TryRemove(strategy.Id, out _);
            _logger.LogWarning(
                "挂条件单失败，客户端评估保持兜底: {StrategyId} {Error}",
                strategy.Id, result.ErrorMessage);
        }
    }

    private async Task ReconcileLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ReconcileInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await ReconcileSafeAsync().ConfigureAwait(false);
        }
    }

    private void OnModeChanged(CryptoTradingMode mode)
    {
        _liveOrderIds.Clear();
        _ = ReconcileSafeAsync();
    }
}
