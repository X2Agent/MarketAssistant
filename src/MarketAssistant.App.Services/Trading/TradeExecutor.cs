using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易执行器，统一的下单入口：风控 → 确认 → 下单 → 记录 → PnL 计算。
/// 同一交易对在任意时刻仅允许一条下单路径进入交易所调用，避免并发重复下单。
/// </summary>
public class TradeExecutor(
    [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
    RiskManager riskManager,
    TradingDataService dataService,
    ILogger<TradeExecutor> logger,
    TradingEnvironmentService? environmentService = null,
    IAlertGate? alertGate = null,
    IAlertCenterService? alertCenterService = null) : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _symbolExecutionLocks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按标的获取执行串行锁（单例字典，调用方共享同一把锁）。
    /// 键统一规范化为大写去空白：下单、条件单挂单、持仓对账都用同一把锁，
    /// 防止"对账读到旧持仓快照 → 交易成交落库 → 对账把成交覆盖回旧值"的竞态。
    /// </summary>
    internal SemaphoreSlim GetExecutionGate(string instrumentSymbol)
        => _symbolExecutionLocks.GetOrAdd(
            instrumentSymbol.Trim().ToUpperInvariant(), _ => new SemaphoreSlim(1, 1));

    private readonly IExchangeClient _exchangeClient = exchangeClient;
    private readonly TradingEnvironmentService? _environmentService = environmentService;
    private readonly RiskManager _riskManager = riskManager;
    private readonly TradingDataService _dataService = dataService;
    private readonly IAlertGate? _alertGate = alertGate;
    private readonly IAlertCenterService? _alertCenterService = alertCenterService;
    private readonly ILogger<TradeExecutor> _logger = logger;

    /// <summary>
    /// Human-in-the-Loop 确认事件。
    /// 当风控返回 NeedsConfirmation 时触发，等待订阅者返回 true（放行）或 false（拒绝）。
    /// 使用事件模式而非单一回调属性，避免单例被多个 ViewModel 订阅时相互覆盖。
    /// 未订阅时保持现有行为（直接拒绝）。
    /// </summary>
    public event Func<string, OrderSide, decimal, decimal, string, Task<bool>>? ConfirmationRequested;

    /// <summary>
    /// 当前是否为合约模式（由活跃交易所客户端决定）。供策略执行器决定语义：
    /// 合约允许双向开仓，现货的 SELL 只能是离场，无法开空。
    /// </summary>
    public bool IsFuturesMode => _exchangeClient.IsFutures;

    /// <summary>
    /// 执行策略触发的交易（兼容包装，委托给 <see cref="ExecuteTriggeredTradeAsync"/>）。
    /// 有效方向/数量直接取自策略本体；
    /// <paramref name="requireClose"/> 表示该触发语义为"平仓退出"（如止损、追踪止损、网格破网、AI 硬性边界）：
    /// 合约模式下若交易所不存在对应方向的持仓则拒绝下单，防止退出型触发在无持仓时反向开出新仓。
    /// </summary>
    /// <remarks>virtual 供单元测试替换（AISignal 硬性边界行为测试）。</remarks>
    public virtual async Task<TradeResult> ExecuteTradeAsync(
        TradingStrategy strategy, decimal currentPrice, string? aiReasoning = null,
        string? pendingCustomParams = null,
        bool requireClose = false,
        CancellationToken ct = default)
    {
        return await ExecuteTriggeredTradeAsync(
            new StrategyTrigger
            {
                Strategy = strategy,
                EffectiveSide = strategy.Side,
                EffectiveQuantity = strategy.Quantity,
                RequireClose = requireClose
            },
            currentPrice, aiReasoning, pendingCustomParams, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行策略触发的交易（无副作用版本）：有效方向/数量与出场语义由
    /// <see cref="StrategyTrigger"/> 承载，策略本体保持只读。
    /// </summary>
    public virtual async Task<TradeResult> ExecuteTriggeredTradeAsync(
        StrategyTrigger trigger, decimal currentPrice, string? aiReasoning = null,
        string? pendingCustomParams = null,
        CancellationToken ct = default)
    {
        var strategy = trigger.Strategy;
        _logger.LogInformation("开始执行交易: {StrategyId} {Symbol} {Side} 数量:{Qty}",

            strategy.Id, strategy.Symbol, trigger.EffectiveSide, trigger.EffectiveQuantity);

        // 限价单基于当前价计算滑点保护价
        decimal? limitPrice = null;
        var orderType = strategy.OrderType;
        if (orderType == OrderType.Limit)
        {
            var slippage = strategy.SlippageTolerance > 0 ? strategy.SlippageTolerance : 0.003m;
            limitPrice = trigger.EffectiveSide == OrderSide.Buy
                ? currentPrice * (1 + slippage)
                : currentPrice * (1 - slippage);
        }

        var result = await ExecuteOrderAsync(
            strategy.Symbol, trigger.EffectiveSide, orderType, trigger.EffectiveQuantity,
            currentPrice, limitPrice: limitPrice, strategyId: strategy.Id,
            aiReasoning: aiReasoning, requireClose: trigger.RequireClose, ct: ct);

        if (result.Success)
        {
            if (pendingCustomParams != null)
                await _dataService.UpdateStrategyTriggeredWithParamsAsync(strategy.Id, pendingCustomParams, ct);
            else
                await _dataService.UpdateStrategyTriggeredAsync(strategy.Id, ct);
        }

        return result;
    }

    /// <summary>
    /// 交易所侧保护性条件单的 clientOrderId 前缀（普通下单为 "MA" 前缀）。
    /// 对账时按前缀识别本应用挂出的条件单，孤儿单可安全撤销。
    /// </summary>
    public const string ConditionOrderClientOrderIdPrefix = "MC";

    /// <summary>
    /// 生成策略条件单的确定性 clientOrderId：前缀 + 策略 ID + 腿别后缀（S=止损 / T=止盈）。
    /// 确定性 ID 使对账可按策略与腿精确匹配挂单，无需在策略表持久化交易所订单 ID。
    /// 币安上限 36 字符：2 + 32 + 1 = 35，双腿也不会超限。
    /// </summary>
    public static string BuildConditionOrderClientOrderId(string strategyId, ConditionOrderLeg leg)
        => ConditionOrderClientOrderIdPrefix + strategyId + (leg == ConditionOrderLeg.Stop ? "S" : "T");

    /// <summary>
    /// 普通下单的 clientOrderId 前缀（条件单用 "MC"）。
    /// </summary>
    public const string OrderClientOrderIdPrefix = "MA";

    /// <summary>
    /// 生成一次性幂等下单 ID（"MA" + 16 位 hex，总长 18 ≤ 币安 36 字符上限）。
    /// 每次调用生成新 ID，同一次调用内的网络重试复用同一 ID：
    /// 币安收到重复的 newClientOrderId 时返回已有订单而非新建，内部重试不会重复下单。
    /// 跨调用的重复下单防护不依赖交易所对幂等 ID 的去重（已完结订单不去重，且
    /// 同键合法重复下单——如手动二连、网格震荡——会被误判为重复）：由"重试耗尽后按
    /// 同一 ID 回查交易所"（<see cref="TryRecoverAmbiguousOrderAsync"/>）区分
    /// "确实没下成"与"下成了但响应丢失"，查不到才让调用方按冷却策略重试。
    /// </summary>
    internal static string BuildClientOrderId()
        => OrderClientOrderIdPrefix + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..16].ToLowerInvariant();

    /// <summary>
    /// 下单响应丢失后的收敛查询：查到订单或交易所明确返回不存在时返回相应结果；
    /// 查询本身失败继续抛出，由调用方标记订单状态未知并冻结自动重试。
    /// </summary>
    private async Task<ExchangeOrderResult?> TryRecoverAmbiguousOrderAsync(
        IExchangeClient exchangeClient,
        string instrumentSymbol,
        string clientOrderId,
        CancellationToken ct)
    {
        try
        {
            var recovered = await exchangeClient
                .GetOrderByClientOrderIdAsync(instrumentSymbol, clientOrderId, ct)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "下单响应丢失但订单已在交易所落地，按幂等ID收敛（未重复下单）: {Symbol} {ClientOrderId} 状态 {Status} 成交量 {Qty}",
                instrumentSymbol, clientOrderId, recovered.Status, recovered.ExecutedQty);

            return recovered;
        }
        catch (OrderNotFoundException)
        {
            _logger.LogInformation(
                "交易所明确确认订单不存在，可按原失败语义重试: {Symbol} {ClientOrderId}",
                instrumentSymbol, clientOrderId);
            return null;
        }
    }

    /// <summary>
    /// 解析追踪止损的回调基点（1% = 100 基点）：优先 CustomParams.trailingPercent，
    /// 缺失时按风险档案兜底（与策略引擎的回退规则一致）。
    /// </summary>
    internal static int ResolveTrailingDeltaBasisPoints(TradingStrategy strategy)
    {
        decimal trailingPercent = 0;
        if (!string.IsNullOrEmpty(strategy.CustomParams))
        {
            try
            {
                using var doc = JsonDocument.Parse(strategy.CustomParams);
                if (doc.RootElement.TryGetProperty("trailingPercent", out var el)
                    && el.TryGetDecimal(out var parsed))
                    trailingPercent = parsed;
            }
            catch (JsonException)
            {
                // 参数损坏按档案兜底，与引擎行为一致
            }
        }

        if (trailingPercent <= 0)
        {
            AISignalParams.TryFromJson(strategy.CustomParams, out var aiParams);
            trailingPercent = ScenarioPresets.GetTrailingPercent(
                aiParams?.ParsedRiskProfile ?? RiskProfile.Balanced);
        }

        return decimal.ToInt32(trailingPercent * 100);
    }

    /// <summary>
    /// 由策略参数与腿别推导条件单规格（方向/订单类型/触发价/回调基点）。
    /// 返回 error 非空表示该策略无法挂条件单（调用方以客户端评估兜底）。
    /// </summary>
    /// <param name="isFutures">
    /// 合约用 STOP_MARKET / TAKE_PROFIT_MARKET；现货用 STOP_LOSS / TAKE_PROFIT——
    /// 币安现货这两种类型触发后同样以 MARKET 成交，不存在"限价=触发价跳空后挂盘不成交"的风险。
    /// </param>
    internal static (OrderSide Side, OrderType? Type, decimal? StopPrice, int? TrailingDelta, string? Error)
        BuildConditionOrderSpec(TradingStrategy strategy, ConditionOrderLeg leg, bool isFutures = true)
    {
        var stopType = isFutures ? OrderType.StopMarket : OrderType.StopLoss;
        var takeProfitType = isFutures ? OrderType.TakeProfitMarket : OrderType.TakeProfit;

        // AI 信号策略的固定护栏：方向取入场方向的反向（多头入场→卖出离场）
        if (strategy.Type == StrategyType.AISignal)
        {
            var exitSide = strategy.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

            if (leg == ConditionOrderLeg.Stop)
            {
                if (strategy.StopLossPrice <= 0)
                    return (exitSide, null, null, null, $"AI 策略缺少止损价，无法挂止损腿: {strategy.Id}");
                return (exitSide, stopType, strategy.StopLossPrice, null, null);
            }

            if (strategy.TakeProfitPrice <= 0)
                return (exitSide, null, null, null, $"AI 策略缺少止盈价，无法挂止盈腿: {strategy.Id}");
            return (exitSide, takeProfitType, strategy.TakeProfitPrice, null, null);
        }

        switch (strategy.Type)
        {
            case StrategyType.StopLoss:
                if (strategy.TriggerPrice <= 0)
                    return (strategy.Side, null, null, null, $"止损策略缺少触发价，无法挂条件单: {strategy.Id}");
                return (strategy.Side, stopType, strategy.TriggerPrice, null, null);

            case StrategyType.TakeProfit when strategy.Side == OrderSide.Sell && leg == ConditionOrderLeg.TakeProfit:
                if (strategy.TriggerPrice <= 0)
                    return (strategy.Side, null, null, null, $"止盈策略缺少触发价，无法挂条件单: {strategy.Id}");
                return (strategy.Side, takeProfitType, strategy.TriggerPrice, null, null);

            case StrategyType.TrailingStop:
                // 现货无 TRAILING_STOP_MARKET：追踪止损在现货只能由客户端评估兜底
                if (!isFutures)
                    return (strategy.Side, null, null, null,
                        $"现货不支持追踪止损条件单，改由客户端评估保护: {strategy.Id}");
                var bps = ResolveTrailingDeltaBasisPoints(strategy);
                // 币安 TrailingStopMarket 回调比例有效范围 0.1%-10%（10-1000 基点）
                if (bps is < 10 or > 1000)
                    return (strategy.Side, null, null, null,
                        $"追踪止损回调比例 {bps} 基点超出币安有效范围(10-1000): {strategy.Id}");
                return (strategy.Side, OrderType.TrailingStopMarket, null, bps, null);

            default:
                return (strategy.Side, null, null, null,
                    $"策略类型 {strategy.Type} 的 {leg} 腿不支持交易所条件单: {strategy.Id}");
        }
    }

    /// <summary>
    /// 为退出型策略挂交易所侧条件单，按腿别挂单：合约为 reduceOnly 的
    /// StopMarket / TrailingStopMarket / TakeProfitMarket，现货为 STOP_LOSS / TAKE_PROFIT
    /// （触发即市价，现货无 reduceOnly 语义——卖出单本身就是减仓）。
    /// 保护性离场不受风控确认与告警联动门限制（与 requireClose 豁免同一原则）；
    /// 挂单本身无成交，持仓 FIFO 与日统计由订单对账路径在成交后回写，
    /// 策略完结由 MarketMonitor 的条件单成交事件驱动。
    /// 现货 AI 策略的"双腿"保护走 <see cref="PlaceSpotProtectiveOcoAsync"/>（OCO 单），
    /// 单腿或合约场景走本方法。
    /// </summary>
    /// <param name="quantityOverride">
    /// 挂单数量覆盖（默认取 strategy.Quantity）。AI 策略由对账方按实际持仓余量传入，
    /// 防止"挂单成交后部分平仓/加仓导致 strategy.Quantity 与实际持仓漂移"时超量卖出。
    /// </param>
    public virtual async Task<TradeResult> PlaceConditionOrderAsync(
        TradingStrategy strategy, ConditionOrderLeg leg,
        decimal? quantityOverride = null, CancellationToken ct = default)
    {
        var exchangeClient = ResolveClientSnapshot();
        var isFutures = exchangeClient.IsFutures;
        var quantity = quantityOverride ?? strategy.Quantity;

        var (side, orderType, stopPrice, trailingDelta, error) =
            BuildConditionOrderSpec(strategy, leg, isFutures);
        if (orderType == null)
            return new TradeResult { Success = false, ErrorMessage = error, FailureCategory = TradeFailureCategory.Rejected };

        var gate = GetExecutionGate(strategy.Symbol);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 确定性 clientOrderId：对账按策略与腿精确匹配
            var clientOrderId = BuildConditionOrderClientOrderId(strategy.Id, leg);

            // 网络异常重试：与普通下单一致的指数退避（1s/2s/4s）。
            // 现货条件单为触发即市价类型，无需 limitPrice
            var response = await PlaceOrderWithRetryAsync(
                token => exchangeClient.PlaceOrderAsync(
                    strategy.Symbol, side, orderType.Value, quantity,
                    price: null, clientOrderId,
                    // 现货没有 reduceOnly：卖出单本身就是减仓，传 true 会被交易所拒绝
                    reduceOnly: isFutures,
                    stopPrice: stopPrice, trailingDelta: trailingDelta, ct: token),
                "挂条件单网络异常，{Attempt}/{Max} 次重试，{Delay}ms 后重试（幂等ID={ClientOrderId}）: {Symbol} {StrategyId}",
                $"挂条件单响应为空（已重试 {OrderRetryCount} 次）",
                ct,
                clientOrderId,
                strategy.Symbol,
                strategy.Id);

            // 挂单成功即落 Pending 交易记录：OrderStateSyncService 对既有对账管线
            // 自动感知成交（回写持仓 FIFO 与日统计），无需新增成交流
            var record = new TradeRecord
            {
                StrategyId = strategy.Id,
                Symbol = strategy.Symbol,
                Side = side,
                OrderType = orderType.Value,
                RequestedQty = quantity,
                ExecutedQty = 0,
                Status = MapStatus(response.Status),
                ExchangeOrderId = long.TryParse(response.OrderId, out var orderId) ? orderId : 0,
                CreatedAt = DateTime.UtcNow
            };
            await _dataService.SaveTradeRecordAsync(record, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "条件单已挂出: {StrategyId} {Type} {Symbol} {Side} 数量:{Qty} 订单ID:{OrderId}",
                strategy.Id, orderType.Value, strategy.Symbol, side, quantity, response.OrderId);

            return new TradeResult { Success = true, Record = record };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "挂条件单失败: {StrategyId} {Symbol}", strategy.Id, strategy.Symbol);
            var category = IsTransient(ex)
                ? TradeFailureCategory.Network
                : TradeFailureCategory.Other;
            return new TradeResult { Success = false, ErrorMessage = ex.Message, FailureCategory = category };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 现货 AI 策略双腿保护的 OCO 下单：止损腿（STOP_LOSS，触发即市价）与止盈腿
    /// （TAKE_PROFIT，触发即市价）打包为一个 orderList，共享同一数量。
    /// 必须用 OCO 而不是两张独立条件单：现货挂单锁定基础资产，两张全量卖单的第二张
    /// 必然因余额被第一张锁定而拒单，止盈腿将永远挂不出去。OCO 只锁定一次数量，
    /// 且任一腿成交后交易所自动撤销另一腿，应用关闭期间保护也不会失效。
    /// 两腿各落一条 Pending 交易记录，对账按各自的 MC 幂等 ID 识别（撤销任一成员单
    /// 即撤销整个列表，币安现货行为）。
    /// </summary>
    public virtual async Task<OcoPlacementResult> PlaceSpotProtectiveOcoAsync(
        TradingStrategy strategy, decimal quantity, CancellationToken ct = default)
    {
        var exchangeClient = ResolveClientSnapshot();
        if (exchangeClient.IsFutures)
        {
            return new OcoPlacementResult
            {
                Success = false,
                Error = "OCO 仅现货支持，合约双腿走 reduceOnly 条件单",
                FailureCategory = TradeFailureCategory.Rejected
            };
        }

        var (stopSide, stopType, stopPrice, _, stopError) =
            BuildConditionOrderSpec(strategy, ConditionOrderLeg.Stop, isFutures: false);
        var (takeProfitSide, takeProfitType, takeProfitPrice, _, takeProfitError) =
            BuildConditionOrderSpec(strategy, ConditionOrderLeg.TakeProfit, isFutures: false);

        if (stopType == null || stopPrice is not > 0)
            return new OcoPlacementResult { Success = false, Error = stopError ?? "止损腿缺少触发价", FailureCategory = TradeFailureCategory.Rejected };
        if (takeProfitType == null || takeProfitPrice is not > 0)
            return new OcoPlacementResult { Success = false, Error = takeProfitError ?? "止盈腿缺少触发价", FailureCategory = TradeFailureCategory.Rejected };
        if (quantity <= 0)
            return new OcoPlacementResult { Success = false, Error = "OCO 数量必须大于 0", FailureCategory = TradeFailureCategory.Rejected };

        var stopClientOrderId = BuildConditionOrderClientOrderId(strategy.Id, ConditionOrderLeg.Stop);
        var takeProfitClientOrderId = BuildConditionOrderClientOrderId(strategy.Id, ConditionOrderLeg.TakeProfit);

        var gate = GetExecutionGate(strategy.Symbol);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双腿方向一致（AI 护栏均为持仓反向离场），以止损腿方向作为 OCO 方向
            var orders = await PlaceOrderWithRetryAsync(
                token => exchangeClient.PlaceSpotOcoAsync(
                    strategy.Symbol, stopSide, quantity,
                    stopClientOrderId, stopPrice.Value,
                    takeProfitClientOrderId, takeProfitPrice.Value, token),
                "挂 OCO 保护单网络异常，{Attempt}/{Max} 次重试，{Delay}ms 后重试（止损腿ID={ClientOrderId}）: {Symbol} {StrategyId}",
                $"挂 OCO 保护单响应为空（已重试 {OrderRetryCount} 次）",
                ct,
                stopClientOrderId,
                strategy.Symbol,
                strategy.Id);

            var stopOrder = orders[0];
            var takeProfitOrder = orders[1];

            var stopRecord = new TradeRecord
            {
                StrategyId = strategy.Id,
                Symbol = strategy.Symbol,
                Side = stopSide,
                OrderType = stopType.Value,
                RequestedQty = quantity,
                ExecutedQty = 0,
                Status = MapStatus(stopOrder.Status),
                ExchangeOrderId = long.TryParse(stopOrder.OrderId, out var stopOrderId) ? stopOrderId : 0,
                CreatedAt = DateTime.UtcNow
            };
            var takeProfitRecord = new TradeRecord
            {
                StrategyId = strategy.Id,
                Symbol = strategy.Symbol,
                Side = takeProfitSide,
                OrderType = takeProfitType.Value,
                RequestedQty = quantity,
                ExecutedQty = 0,
                Status = MapStatus(takeProfitOrder.Status),
                ExchangeOrderId = long.TryParse(takeProfitOrder.OrderId, out var tpOrderId) ? tpOrderId : 0,
                CreatedAt = DateTime.UtcNow
            };
            await _dataService.SaveTradeRecordAsync(stopRecord, ct).ConfigureAwait(false);
            await _dataService.SaveTradeRecordAsync(takeProfitRecord, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "现货 OCO 保护单已挂出: {StrategyId} {Symbol} 数量:{Qty} 止损:{Stop} 止盈:{TakeProfit} 订单ID:{StopOrderId}/{TpOrderId}",
                strategy.Id, strategy.Symbol, quantity, stopPrice, takeProfitPrice,
                stopOrder.OrderId, takeProfitOrder.OrderId);

            return new OcoPlacementResult
            {
                Success = true,
                StopRecord = stopRecord,
                TakeProfitRecord = takeProfitRecord
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "挂 OCO 保护单失败: {StrategyId} {Symbol}", strategy.Id, strategy.Symbol);
            var category = IsTransient(ex)
                ? TradeFailureCategory.Network
                : TradeFailureCategory.Other;
            return new OcoPlacementResult { Success = false, Error = ex.Message, FailureCategory = category };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 通用下单方法，所有交易路径（策略触发、AI Agent、手动）的统一入口。
    /// 风控检查和人工确认在 symbol 锁之外执行，避免等待用户输入时锁死后续交易。
    /// <paramref name="requireClose"/> 表示调用方要求本笔交易必须是平仓（无对应持仓时拒绝），
    /// 用于止损、追踪止损、网格破网、AI 硬性边界等退出型触发，防止合约模式反向开仓。
    /// </summary>
    public async Task<TradeResult> ExecuteOrderAsync(
        string instrumentSymbol, OrderSide side, OrderType type, decimal quantity,
        decimal currentPrice, decimal? limitPrice = null,
        string strategyId = "manual", string? aiReasoning = null,
        bool requireClose = false,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(instrumentSymbol))
        {
            instrumentSymbol = CryptoSymbolConverter.ToBinanceFormat(instrumentSymbol);
        }

        // 环境快照：一次下单的判断（IsFutures/持仓/杠杆/下单）必须全部落在同一个客户端上。
        // RoutingExchangeClient 每次调用独立解析活跃客户端，若无快照，用户在风控/确认等待期间
        // 切换模拟盘/实盘会让这笔订单落到错误环境（真金白银场景下不可接受）。
        var exchangeClient = ResolveClientSnapshot();
        var modeAtEntry = _environmentService?.CurrentMode;

        // 风控校验和人工确认在 symbol 锁外完成，防止 ConfirmationCallback 等待期间
        // 持有 SemaphoreSlim，导致同一标的后续所有交易永久阻塞。
        // 退出型触发（止损/止盈/网格破网/AI 硬边界）透传 requireClose：风控据此放行减仓单，
        // 避免熔断期间止损被拒绝、持仓失去保护
        var riskCheck = await _riskManager.ValidateOrderAsync(
            instrumentSymbol, side, quantity, currentPrice, type, strategyId, requireClose, ct);

        if (riskCheck.NeedsConfirmation)
        {
            _logger.LogWarning("交易需人工确认: {InstrumentSymbol} {Side} 金额:{Amount}",
                instrumentSymbol, side, quantity * currentPrice);

            if (ConfirmationRequested != null)
            {
                var approved = await ConfirmationRequested.Invoke(
                    instrumentSymbol, side, currentPrice, quantity, riskCheck.Reason ?? "需人工确认");
                if (!approved)
                    return new TradeResult
                    {
                        Success = false,
                        ErrorMessage = $"用户拒绝交易: {riskCheck.Reason}",
                        FailureCategory = TradeFailureCategory.Rejected
                    };

                _logger.LogInformation("用户已确认交易: {InstrumentSymbol} {Side}", instrumentSymbol, side);
            }
            else
            {
                return new TradeResult
                {
                    Success = false,
                    ErrorMessage = $"需人工确认: {riskCheck.Reason}",
                    FailureCategory = TradeFailureCategory.Rejected
                };
            }
        }
        else if (!riskCheck.Passed)
        {
            _logger.LogWarning("风控拒绝: {Reason}", riskCheck.Reason);
            return new TradeResult
            {
                Success = false,
                ErrorMessage = $"风控拒绝: {riskCheck.Reason}",
                FailureCategory = TradeFailureCategory.Rejected
            };
        }
        else if (!requireClose && _alertGate?.IsGated(MarketType.Crypto, instrumentSymbol) == true)
        {
            // 告警交易联动：确认级告警触发期间，该标的的新开仓信号强制升级为人工确认。
            // requireClose 的退出型触发（止损/止盈等保护性平仓）豁免，确保告警期间仍能及时离场。
            _logger.LogWarning("存在触发中的确认级告警，强制人工确认: {Symbol} {Side}", instrumentSymbol, side);

            if (ConfirmationRequested != null)
            {
                var approved = await ConfirmationRequested.Invoke(
                    instrumentSymbol, side, currentPrice, quantity,
                    "该标的当前存在触发中的紧急告警，交易需人工确认");
                if (!approved)
                    return new TradeResult
                    {
                        Success = false,
                        ErrorMessage = "用户拒绝交易: 存在触发中的确认级告警",
                        FailureCategory = TradeFailureCategory.Rejected
                    };

                _logger.LogInformation("用户已确认告警联动交易: {Symbol} {Side}", instrumentSymbol, side);
            }
            else
            {
                return new TradeResult
                {
                    Success = false,
                    ErrorMessage = "存在触发中的确认级告警，需人工确认（无确认订阅者）",
                    FailureCategory = TradeFailureCategory.Rejected
                };
            }
        }

        // 仅在实际调用交易所 API 时持有 symbol 锁，防止同一标的并发重复下单
        var gate = GetExecutionGate(instrumentSymbol);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 快照一致性复检：等待风控/用户确认期间交易模式可能已被切换，
            // 与入口快照不一致时拒绝本笔订单，绝不把它路由到另一个环境
            if (modeAtEntry.HasValue && _environmentService?.CurrentMode is { } currentMode && currentMode != modeAtEntry.Value)
            {
                _logger.LogError(
                    "交易模式在确认期间被切换（{EntryMode} → {CurrentMode}），拒绝下单: {Symbol} {Side}",
                    modeAtEntry.Value, currentMode, instrumentSymbol, side);
                return new TradeResult
                {
                    Success = false,
                    ErrorMessage = $"交易模式在确认期间被切换（{modeAtEntry.Value} → {currentMode}），本笔订单已取消，请重新发起",
                    FailureCategory = TradeFailureCategory.Rejected
                };
            }

            // 锁内复检：风控校验与人工确认在锁外完成（防确认等待期间锁死同一标的），
            // 等待期间同一标的的其他现货卖出可能已消耗本地 FIFO 持仓。两笔并发卖出若都基于
            // 同一份持仓快照通过风控，会依次成交造成超卖与负持仓，故获取锁后必须重验。
            // 合约模式以交易所持仓为准且 reduceOnly 由交易所强制，无需本地复检。
            if (side == OrderSide.Sell && !exchangeClient.IsFutures)
            {
                var openPositions = await _dataService.GetOpenPositionsAsync(instrumentSymbol, ct).ConfigureAwait(false);
                var availableQuantity = openPositions
                    .Where(p => p.Symbol.Equals(instrumentSymbol, StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.RemainingQuantity);

                if (quantity > availableQuantity)
                {
                    _logger.LogWarning(
                        "锁内复检拒绝：{Symbol} 可平数量 {Available} 少于本次卖出 {Quantity}（并发卖出或确认等待期间持仓已变化）",
                        instrumentSymbol, availableQuantity, quantity);
                    return new TradeResult
                    {
                        Success = false,
                        ErrorMessage = $"并发校验失败：{instrumentSymbol} 可平数量 {availableQuantity} 少于本次卖出数量 {quantity}",
                        FailureCategory = TradeFailureCategory.Rejected
                    };
                }
            }

            return await ExecuteApprovedOrderAsync(
                exchangeClient, modeAtEntry,
                instrumentSymbol, side, type, quantity, currentPrice, limitPrice,
                strategyId, aiReasoning, requireClose, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 解析当前活跃客户端快照：RoutingExchangeClient 按当前模式路由，
    /// 其他实现（单元测试 Mock）原样返回。
    /// </summary>
    /// <summary>
    /// 紧急平仓（kill switch 的"全部平仓"路径）：绕过风控校验与人工确认——
    /// 紧急场景下等确认等于把用户暴露在风险中——但保留客户端快照一致性、symbol 锁、
    /// 合约开平仓判定、网络重试、成交记录与持仓回写等全部资金安全机制。
    /// </summary>
    public virtual async Task<TradeResult> ExecuteEmergencyCloseAsync(
        string instrumentSymbol, OrderSide side, decimal quantity, decimal currentPrice,
        CancellationToken ct = default,
        TradingMode? expectedMode = null)
    {
        _logger.LogWarning(
            "执行紧急平仓: {Symbol} {Side} 数量 {Qty} 价格 {Price}",
            instrumentSymbol, side, quantity, currentPrice);

        var exchangeClient = ResolveClientSnapshot();
        var modeAtEntry = expectedMode ?? _environmentService?.CurrentMode;

        var gate = GetExecutionGate(instrumentSymbol);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (modeAtEntry.HasValue && _environmentService != null && _environmentService.CurrentMode != modeAtEntry.Value)
            {
                _logger.LogError(
                    "紧急平仓期间交易模式被切换（{EntryMode} → {CurrentMode}），中止该标的平仓: {Symbol}",
                    modeAtEntry.Value, _environmentService.CurrentMode, instrumentSymbol);
                return new TradeResult
                {
                    Success = false,
                    ErrorMessage = $"紧急平仓期间交易模式被切换（{modeAtEntry.Value} → {_environmentService.CurrentMode}），本笔平仓已中止",
                    FailureCategory = TradeFailureCategory.Rejected
                };
            }

            return await ExecuteApprovedOrderAsync(
                exchangeClient, modeAtEntry,
                instrumentSymbol, side, OrderType.Market, quantity, currentPrice, null,
                EmergencyStopService.EmergencyCloseStrategyId,
                "紧急停止：全部平仓",
                requireClose: true,
                ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static IExchangeClient ResolveClientSnapshot(IExchangeClient client)
        => client is RoutingExchangeClient router ? router.GetActiveClientSnapshot() : client;

    /// <summary>
    /// 成交滑点超过风控阈值时告警（仅提示，不阻塞已完成的成交）。
    /// 风控配置读取失败时跳过告警——不得让告警路径影响成交主链。
    /// </summary>
    private async Task ReportSlippageAsync(
        string instrumentSymbol, decimal slippagePercent, decimal executedPrice, decimal signalPrice,
        CancellationToken ct)
    {
        try
        {
            var config = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);
            if (config.MaxSlippagePercent <= 0 || slippagePercent <= config.MaxSlippagePercent)
                return;

            if (_alertCenterService == null)
                return;

            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = instrumentSymbol,
                Level = AlertLevel.Warning,
                Source = AlertSource.Risk,
                Title = "成交滑点超限",
                Content = $"{instrumentSymbol} 市价单成交价 {executedPrice} 相对信号价 {signalPrice} 偏离 "
                          + $"{slippagePercent:F2}%，超过阈值 {config.MaxSlippagePercent:F2}%。"
                          + "流动性不足时建议改用限价单"
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "滑点告警上报失败: {Symbol}", instrumentSymbol);
        }
    }

    private IExchangeClient ResolveClientSnapshot() => ResolveClientSnapshot(_exchangeClient);

    private async Task<TradeResult> ExecuteApprovedOrderAsync(
        IExchangeClient exchangeClient, TradingMode? modeAtEntry,
        string instrumentSymbol, OrderSide side, OrderType type, decimal quantity,
        decimal currentPrice, decimal? limitPrice,
        string strategyId, string? aiReasoning, bool requireClose,
        CancellationToken ct)
    {
        try
        {
            // 幂等下单 ID："MA" + 16 位 hex（≤ 币安 36 字符上限），每次调用生成新 ID。
            // 同一次调用内的网络异常重试复用该 ID：币安收到重复的 newClientOrderId 时返回已有订单
            // 而非新建，内部重试不会重复下单。跨调用的收敛由下方"重试耗尽后按同一 ID 回查"保证。
            var clientOrderId = BuildClientOrderId();

            // 合约模式：判断本次操作是开仓还是平仓
            // 平仓 = 持有多头时卖出 / 持有空头时买入，需要 reduceOnly=true
            var isFutures = exchangeClient.IsFutures;
            var reduceOnly = false;
            if (isFutures)
            {
                // 持仓查询失败时无法判定开/平仓：默认按开仓会把平仓单变成反向开仓（10x 风险敞口），
                // 必须中止本笔订单（fail-closed），按网络类失败走短冷却后重试
                var closePosition = await IsClosePositionAsync(exchangeClient, instrumentSymbol, side, ct);
                if (!closePosition.HasValue)
                {
                    _logger.LogError(
                        "查询合约持仓失败，无法判定开/平仓，拒绝下单以免平仓变反向开仓: {Symbol} {Side}",
                        instrumentSymbol, side);
                    return new TradeResult
                    {
                        Success = false,
                        ErrorMessage = $"无法确认 {instrumentSymbol} 合约持仓状态，本笔订单已取消，请稍后重试",
                        FailureCategory = TradeFailureCategory.Network
                    };
                }
                reduceOnly = closePosition.Value;

                // 退出型触发要求本笔为平仓：交易所无对应方向持仓时拒绝，
                // 防止止损/追踪止损/网格破网/AI 硬性边界在持仓已平后反向开出新仓。
                if (requireClose && !reduceOnly)
                {
                    _logger.LogWarning(
                        "退出型触发要求平仓但 {Symbol} 无对应方向持仓，拒绝下单以免反向开仓: {Side} {Qty}",
                        instrumentSymbol, side, quantity);
                    return new TradeResult
                    {
                        Success = false,
                        ErrorMessage = $"策略要求平仓但 {instrumentSymbol} 无对应方向持仓，拒绝下单",
                        FailureCategory = TradeFailureCategory.Rejected
                    };
                }

                // 合约开仓前设置杠杆：倍率取自风控配置（默认 10x），
                // 避免使用交易所默认的 20x 导致强平风险过高，同时允许用户按风险承受能力调整
                if (!reduceOnly)
                {
                    // 杠杆必须先设置成功再下单：失败时沿用交易所当前杠杆（可能是 20x 默认值），
                    // 用户以为自己在 10x 而实际承担 2 倍风险，属于不可接受的静默偏差。
                    // 开仓因此 fail-closed；平仓（reduceOnly）不受影响，减仓不应被杠杆问题阻塞。
                    var riskConfig = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);
                    var leverage = Math.Clamp(riskConfig.FuturesLeverage, MinFuturesLeverage, MaxFuturesLeverage);
                    try
                    {
                        await exchangeClient.SetLeverageAsync(instrumentSymbol, leverage, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex,
                            "设置合约杠杆失败，拒绝开仓（避免以交易所默认杠杆承担未预期风险）: {Symbol} 目标 {Leverage}x",
                            instrumentSymbol, leverage);
                        return new TradeResult
                        {
                            Success = false,
                            ErrorMessage = $"无法设置 {instrumentSymbol} 杠杆为 {leverage}x，本笔开仓已取消，请稍后重试",
                            FailureCategory = TradeFailureCategory.Network
                        };
                    }
                }
            }

            // 网络异常重试：最多 3 次，指数退避 1s/2s/4s。
            // 业务错误（如余额不足、风控拒绝）不重试，直接抛出。
            ExchangeOrderResult? response;
            try
            {
                response = await PlaceOrderWithRetryAsync(
                    token => exchangeClient.PlaceOrderAsync(
                        instrumentSymbol, side, type, quantity,
                        type is OrderType.Limit or OrderType.PostOnlyLimit ? limitPrice : null,
                        clientOrderId, reduceOnly,
                        stopPrice: null, trailingDelta: null, ct: token),
                    "下单网络异常，{Attempt}/{Max} 次重试，{Delay}ms 后重试（幂等ID={ClientOrderId}）: {Symbol} {Side}",
                    $"交易所下单响应为空（已重试 {OrderRetryCount} 次）",
                    ct,
                    clientOrderId,
                    instrumentSymbol,
                    side);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                // 重试耗尽仍拿不到响应：请求可能已落地，只是响应在网络上丢了。
                // 只有交易所明确确认订单不存在，才允许调用方按网络失败冷却后重试。
                try
                {
                    response = await TryRecoverAmbiguousOrderAsync(exchangeClient, instrumentSymbol, clientOrderId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception recoveryException) when (recoveryException is not OperationCanceledException)
                {
                    _logger.LogError(recoveryException,
                        "订单收敛查询失败，冻结自动重试并等待人工对账: {Symbol} {ClientOrderId}",
                        instrumentSymbol, clientOrderId);
                    return new TradeResult
                    {
                        Success = false,
                        ErrorMessage = $"无法确认订单是否已落地（幂等ID={clientOrderId}）。已停止自动重试，请在交易所核对订单后人工处理",
                        FailureCategory = TradeFailureCategory.OrderStateUnknown
                    };
                }

                if (response == null)
                    throw;
            }

            var record = new TradeRecord
            {
                StrategyId = strategyId,
                Symbol = instrumentSymbol,
                Side = side,
                OrderType = type,
                RequestedQty = response.RequestedQty == 0 ? quantity : response.RequestedQty,
                ExecutedQty = response.ExecutedQty,
                RequestedPrice = limitPrice,
                // 市价单 response.Price 通常为 0，优先用成交均价（合约 avgPrice），其次用 cummulativeQuoteQty/executedQty 计算
                ExecutedPrice = CalculateExecutedPrice(response, currentPrice),
                Commission = response.FillCommission,
                CommissionAsset = response.CommissionAsset ?? string.Empty,
                Status = MapStatus(response.Status),
                ExchangeOrderId = long.TryParse(response.OrderId, out var orderId) ? orderId : 0,
                AIReasoning = aiReasoning,
                CompletedAt = response.Status == "FILLED" ? DateTime.UtcNow : null
            };

            // 成交滑点检测：市价单无法事前限制滑点，只能事后发现并提示。
            // 限价单的滑点由用户设定的 SlippageTolerance 事前约束，不重复判定。
            // 必须在落库前赋值：仓库层只在 INSERT 写该列，后续 UPDATE 不覆盖，
            // 落在保存之后会导致 slippage_percent 恒为空
            decimal? slippagePercent = null;
            if (type == OrderType.Market && currentPrice > 0 && record.ExecutedPrice > 0)
            {
                slippagePercent = Math.Abs(record.ExecutedPrice - currentPrice) / currentPrice * 100m;
                record.SlippagePercent = slippagePercent;
            }

            await _dataService.SaveTradeRecordAsync(record, ct);

            // 告警在落库之后：告警链路失败不得影响已完成的成交记录
            if (slippagePercent.HasValue)
            {
                await ReportSlippageAsync(
                    instrumentSymbol, slippagePercent.Value, record.ExecutedPrice, currentPrice, ct)
                    .ConfigureAwait(false);
            }

            // 持仓追踪与 PnL 计算：现货用本地 FIFO，合约基于交易所持仓
            decimal pnl = 0;
            if (record.ExecutedQty > 0)
            {
                if (isFutures)
                {
                    pnl = await UpdateFuturesPositionAsync(
                        instrumentSymbol, side, record.ExecutedQty, record.ExecutedPrice,
                        strategyId, record.CreatedAt, reduceOnly, ct);
                }
                else
                {
                    pnl = await UpdateSpotPositionFifoAsync(
                        instrumentSymbol, side, record.ExecutedQty, record.ExecutedPrice,
                        strategyId, record.CreatedAt, ct);
                }
            }
            // 仅在实际有成交（首次填单）时计入交易次数；未成交订单不计，
            // 后续部分成交增量由对账路径补充统计，避免同一订单重复计数
            await _dataService.UpdateDailyStatsAsync(pnl, record.Commission,
                countTrade: record.ExecutedQty > 0, ct);

            // 已实现盈亏回写到记录：绩效统计（胜率/盈亏比/最大回撤）以该列为数据基础。
            // 落库在持仓结算之后，结算失败时记录保持 RealizedPnl=0（未知），不得猜测
            if (pnl != 0)
                await _dataService.UpdateTradeRecordRealizedPnlAsync(record.Id, pnl, ct);

            _logger.LogInformation("交易执行成功: {StrategyId} 订单ID:{OrderId} 状态:{Status} PnL:{Pnl}",
                strategyId, response.OrderId, response.Status, pnl);

            return new TradeResult { Success = true, Record = record };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "交易执行失败: {InstrumentSymbol} {Side}", instrumentSymbol, side);
            // 网络类异常（含重试耗尽）短期可恢复；其余归为其他失败由调用方按冷却策略处理。
            // 交易所异常统一被包装成 FriendlyException，必须沿 InnerException 链递归判定。
            var category = IsTransient(ex)
                ? TradeFailureCategory.Network
                : TradeFailureCategory.Other;
            return new TradeResult { Success = false, ErrorMessage = ex.Message, FailureCategory = category };
        }
    }

    /// <summary>
    /// 递归判定异常是否为瞬时（网络/超时）类，沿 InnerException 链检查。
    /// 交易所客户端会把 HttpRequestException 包装成 FriendlyException 抛出，
    /// 仅判断顶层类型会让重试与冷却分类全部失效。
    /// 用户主动取消的 TaskCanceledException（令牌已取消）不算瞬时——重试过滤器
    /// 另有 !ct.IsCancellationRequested 守卫，取消会直接落入 Other 类别而非重试。
    /// </summary>
    internal static bool IsTransient(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException or TimeoutException or SocketException)
                return true;
            // 令牌未被取消的 TaskCanceledException = HttpClient 超时触发的取消，属瞬时网络问题
            if (current is TaskCanceledException taskCanceled && !taskCanceled.CancellationToken.IsCancellationRequested)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 下单重试次数：指数退避 1s/2s/4s，合计 3 次尝试。
    /// </summary>
    private const int OrderRetryCount = 3;

    /// <summary>
    /// 下单重试的退避延迟工厂（入参为延迟毫秒数），默认真实等待。
    /// internal 测试缝：重试用例注入零延迟，避免指数退避拖慢测试。
    /// </summary>
    internal Func<int, CancellationToken, Task>? RetryDelayFactory { get; set; }

    /// <summary>
    /// 带指数退避的下单重试，收敛条件单与普通下单两条路径的同一段重试逻辑。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 仅瞬态网络异常参与重试；业务错误（余额不足、风控拒绝）不重试直接抛出。
    /// 重复提交安全由调用方传入的 clientOrderId 保证：交易所按幂等ID 去重，
    /// 重试不会产生第二笔挂单。
    /// </para>
    /// <para>
    /// 重试耗尽仍未拿到响应时抛出 <see cref="InvalidOperationException"/>
    /// （携带最后一次网络异常），由调用方的 catch 归类为 Network 失败——
    /// 该类失败短期可恢复，调用方按冷却策略处理而非直接停用策略。
    /// </para>
    /// </remarks>
    /// <param name="place">下单委托。</param>
    /// <param name="retryLogMessage">重试警告日志模板，须依次含 {Attempt}/{Max}/{Delay}/{ClientOrderId} 占位符。</param>
    /// <param name="emptyResponseMessage">重试耗尽且响应为空时的异常文案。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="clientOrderId">幂等下单 ID，随重试日志透出。</param>
    /// <param name="logContext">日志模板中 {ClientOrderId} 之后的上下文参数。</param>
    private async Task<T> PlaceOrderWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> place,
        string retryLogMessage,
        string emptyResponseMessage,
        CancellationToken ct,
        string clientOrderId,
        params object?[] logContext)
    {
        T? response = default;
        Exception? lastNetworkException = null;

        for (var attempt = 1; attempt <= OrderRetryCount; attempt++)
        {
            try
            {
                response = await place(ct).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
            {
                lastNetworkException = ex;
                if (attempt >= OrderRetryCount)
                    break;

                var delayMs = (int)Math.Pow(2, attempt - 1) * 1000;

                // 前缀四参（尝试序号/总次数/退避时长/幂等ID）拼接调用方上下文，与模板占位符对齐
                var logArgs = new object?[4 + logContext.Length];
                logArgs[0] = attempt;
                logArgs[1] = OrderRetryCount;
                logArgs[2] = delayMs;
                logArgs[3] = clientOrderId;
                logContext.CopyTo(logArgs, 4);
                _logger.LogWarning(ex, retryLogMessage, logArgs);

                await (RetryDelayFactory ?? Task.Delay)(delayMs, ct).ConfigureAwait(false);
            }
        }

        if (response is not null)
            return response;

        throw new InvalidOperationException(emptyResponseMessage, lastNetworkException);
    }

    /// <summary>合约杠杆合法区间（币安 1-125 倍），配置越界时钳制。</summary>
    private const int MinFuturesLeverage = 1;
    private const int MaxFuturesLeverage = 125;

    /// <summary>
    /// 判断合约交易方向是否为平仓操作。
    /// 持有多头（PositionAmt > 0）时卖出 = 平多
    /// 持有空头（PositionAmt < 0）时买入 = 平空
    /// 返回 null 表示持仓查询失败、开/平仓无法判定（调用方必须中止下单）。
    /// </summary>
    private async Task<bool?> IsClosePositionAsync(IExchangeClient exchangeClient, string symbol, OrderSide side, CancellationToken ct)
    {
        try
        {
            var positions = await exchangeClient.GetPositionsAsync(symbol, ct);
            foreach (var pos in positions)
            {
                if (!string.Equals(pos.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    continue;

                var posAmt = pos.PositionAmt;
                if (posAmt > 0 && side == OrderSide.Sell)
                    return true; // 平多
                if (posAmt < 0 && side == OrderSide.Buy)
                    return true; // 平空
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询合约持仓失败，无法判断是否为平仓: {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// 现货 FIFO 持仓追踪：买入开多仓，卖出按 FIFO 平仓计算已实现盈亏。
    /// </summary>
    private async Task<decimal> UpdateSpotPositionFifoAsync(
        string symbol, OrderSide side, decimal executedQty, decimal executedPrice,
        string strategyId, DateTime openedAt, CancellationToken ct)
    {
        if (side == OrderSide.Buy)
        {
            await _dataService.OpenPositionAsync(new Position
            {
                Symbol = symbol,
                Side = PositionSide.Long,
                Quantity = executedQty,
                EntryPrice = executedPrice,
                StrategyId = strategyId,
                OpenedAt = openedAt
            }, ct);
            return 0;
        }

        return await _dataService.ClosePositionFifoAsync(symbol, executedQty, executedPrice, ct);
    }

    /// <summary>
    /// 合约持仓追踪与 PnL 计算。
    /// 开仓时记录本地持仓；平仓时按 FIFO 逐笔匹配开仓记录计算已实现盈亏。
    /// 相比查询交易所剩余持仓均价（分批开仓时部分平仓会失真），
    /// 本地 FIFO 能精确匹配每笔开仓价格；手续费单独计入日统计，不在 PnL 中重复扣减。
    /// </summary>
    private async Task<decimal> UpdateFuturesPositionAsync(
        string symbol, OrderSide side, decimal executedQty, decimal executedPrice,
        string strategyId, DateTime openedAt,
        bool isClose, CancellationToken ct)
    {
        if (!isClose)
        {
            // 开仓：记录持仓方向（多头买入/空头卖出）
            var positionSide = side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;
            await _dataService.OpenPositionAsync(new Position
            {
                Symbol = symbol,
                Side = positionSide,
                Quantity = executedQty,
                EntryPrice = executedPrice,
                StrategyId = strategyId,
                OpenedAt = openedAt
            }, ct);
            return 0;
        }

        // 平仓：卖出平多匹配多头持仓，买入平空匹配空头持仓
        var closeSide = side == OrderSide.Sell ? PositionSide.Long : PositionSide.Short;
        return await _dataService.ClosePositionFifoAsync(symbol, executedQty, executedPrice, ct, closeSide);
    }

    /// <summary>
    /// 计算实际成交价：优先用合约 avgPrice，其次用 CumulativeQuoteQty/executedQty，最后用当前价兜底。
    /// </summary>
    private static decimal CalculateExecutedPrice(ExchangeOrderResult response, decimal currentPrice)
    {
        // 合约订单响应可能包含 avgPrice 字段
        if (response.AveragePrice > 0)
            return response.AveragePrice;

        // 从成交金额和成交量计算实际成交均价
        if (response.CumulativeQuoteQty > 0 && response.ExecutedQty > 0)
            return response.CumulativeQuoteQty / response.ExecutedQty;

        // 市价单 response.Price 通常为 0，用当前价兜底
        return response.Price == 0 ? currentPrice : response.Price;
    }

    private static TradeRecordStatus MapStatus(string status) => status switch
    {
        "FILLED" => TradeRecordStatus.Filled,
        "PARTIALLY_FILLED" => TradeRecordStatus.PartiallyFilled,
        "CANCELED" or "CANCELLED" => TradeRecordStatus.Cancelled,
        "REJECTED" or "EXPIRED" => TradeRecordStatus.Failed,
        _ => TradeRecordStatus.Pending
    };

    /// <summary>
    /// 释放所有 symbol 执行锁资源，避免长期运行后内存泄漏。
    /// </summary>
    public void Dispose()
    {
        foreach (var kvp in _symbolExecutionLocks)
            kvp.Value.Dispose();
        _symbolExecutionLocks.Clear();
        GC.SuppressFinalize(this);
    }
}
