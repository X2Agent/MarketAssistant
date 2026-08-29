using System.Collections.Concurrent;
using System.Net.Sockets;
using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易执行器，统一的下单入口：风控 → 确认 → 下单 → 记录 → PnL 计算。
/// 同一交易对在任意时刻仅允许一条下单路径进入交易所调用，避免并发重复下单。
/// </summary>
public class TradeExecutor : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _symbolExecutionLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IExchangeClient _exchangeClient;
    private readonly TradingEnvironmentService? _environmentService;
    private readonly RiskManager _riskManager;
    private readonly TradingDataService _dataService;
    private readonly ILogger<TradeExecutor> _logger;

    /// <summary>
    /// Human-in-the-Loop 确认事件。
    /// 当风控返回 NeedsConfirmation 时触发，等待订阅者返回 true（放行）或 false（拒绝）。
    /// 使用事件模式而非单一回调属性，避免单例被多个 ViewModel 订阅时相互覆盖。
    /// 未订阅时保持现有行为（直接拒绝）。
    /// </summary>
    public event Func<string, OrderSide, decimal, decimal, string, Task<bool>>? ConfirmationRequested;

    public TradeExecutor(
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        RiskManager riskManager,
        TradingDataService dataService,
        ILogger<TradeExecutor> logger,
        TradingEnvironmentService? environmentService = null)
    {
        _exchangeClient = exchangeClient;
        _riskManager = riskManager;
        _dataService = dataService;
        _logger = logger;
        _environmentService = environmentService;
    }

    /// <summary>
    /// 执行策略触发的交易（委托给通用下单方法）。
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
        _logger.LogInformation("开始执行交易: {StrategyId} {Symbol} {Side} 数量:{Qty}",

            strategy.Id, strategy.Symbol, strategy.Side, strategy.Quantity);

        // 限价单基于当前价计算滑点保护价
        decimal? limitPrice = null;
        var orderType = strategy.OrderType;
        if (orderType == OrderType.Limit)
        {
            var slippage = strategy.SlippageTolerance > 0 ? strategy.SlippageTolerance : 0.003m;
            limitPrice = strategy.Side == OrderSide.Buy
                ? currentPrice * (1 + slippage)
                : currentPrice * (1 - slippage);
        }

        var result = await ExecuteOrderAsync(
            strategy.Symbol, strategy.Side, orderType, strategy.Quantity,
            currentPrice, limitPrice: limitPrice, strategyId: strategy.Id,
            aiReasoning: aiReasoning, requireClose: requireClose, ct: ct);

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
        // 环境快照：一次下单的判断（IsFutures/持仓/杠杆/下单）必须全部落在同一个客户端上。
        // RoutingExchangeClient 每次调用独立解析活跃客户端，若无快照，用户在风控/确认等待期间
        // 切换模拟盘/实盘会让这笔订单落到错误环境（真金白银场景下不可接受）。
        var exchangeClient = ResolveClientSnapshot();
        var modeAtEntry = _environmentService?.CurrentMode;

        // 风控校验和人工确认在 symbol 锁外完成，防止 ConfirmationCallback 等待期间
        // 持有 SemaphoreSlim，导致同一标的后续所有交易永久阻塞。
        var riskCheck = await _riskManager.ValidateOrderAsync(instrumentSymbol, side, quantity, currentPrice, type, ct);

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

        // 仅在实际调用交易所 API 时持有 symbol 锁，防止同一标的并发重复下单
        var gate = _symbolExecutionLocks.GetOrAdd(
            instrumentSymbol.Trim(), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 快照一致性复检：等待风控/用户确认期间交易模式可能已被切换，
            // 与入口快照不一致时拒绝本笔订单，绝不把它路由到另一个环境
            if (modeAtEntry.HasValue && _environmentService!.CurrentMode != modeAtEntry.Value)
            {
                _logger.LogError(
                    "交易模式在确认期间被切换（{EntryMode} → {CurrentMode}），拒绝下单: {Symbol} {Side}",
                    modeAtEntry.Value, _environmentService.CurrentMode, instrumentSymbol, side);
                return new TradeResult
                {
                    Success = false,
                    ErrorMessage = $"交易模式在确认期间被切换（{modeAtEntry.Value} → {_environmentService.CurrentMode}），本笔订单已取消，请重新发起",
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
    private static IExchangeClient ResolveClientSnapshot(IExchangeClient client)
        => client is RoutingExchangeClient router ? router.GetActiveClientSnapshot() : client;

    private IExchangeClient ResolveClientSnapshot() => ResolveClientSnapshot(_exchangeClient);

    private async Task<TradeResult> ExecuteApprovedOrderAsync(
        IExchangeClient exchangeClient, CryptoTradingMode? modeAtEntry,
        string instrumentSymbol, OrderSide side, OrderType type, decimal quantity,
        decimal currentPrice, decimal? limitPrice,
        string strategyId, string? aiReasoning, bool requireClose,
        CancellationToken ct)
    {
        try
        {
            // 生成订单客户端 ID（"MA" + 16 位 hex，总长 18 ≤ 币安 36 字符上限）。
            // 同一次下单内的网络异常重试复用该 ID：币安收到重复的 newClientOrderId 时返回已有订单而非新建，避免重复下单。
            // 注意：幂等性仅覆盖本方法的内部重试循环；跨调用重试（如人工重发）会生成新 ID。
            var clientOrderId = "MA" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..16].ToLowerInvariant();

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

                // 合约开仓前设置默认杠杆（10x），避免使用交易所默认的 20x 导致强平风险过高
                if (!reduceOnly)
                {
                    try
                    {
                        await exchangeClient.SetLeverageAsync(instrumentSymbol, DefaultFuturesLeverage, ct);
                    }
                    catch (Exception ex)
                    {
                        // 杠杆设置失败不应阻止下单，使用交易所当前杠杆继续
                        _logger.LogWarning(ex, "设置合约杠杆失败，使用交易所当前杠杆: {Symbol}", instrumentSymbol);
                    }
                }
            }

            // 网络异常重试：最多 3 次，指数退避 1s/2s/4s。
            // 业务错误（如余额不足、风控拒绝）不重试，直接抛出。
            ExchangeOrderResult? response = null;
            Exception? lastNetworkException = null;
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    response = await exchangeClient.PlaceOrderAsync(
                        instrumentSymbol, side, type, quantity,
                        type == OrderType.Limit ? limitPrice : null,
                        clientOrderId, reduceOnly,
                        stopPrice: null, trailingDelta: null, ct: ct);
                    break;
                }
                catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
                {
                    lastNetworkException = ex;
                    if (attempt >= maxRetries)
                        break;

                    var delayMs = (int)Math.Pow(2, attempt - 1) * 1000;
                    _logger.LogWarning(ex,
                        "下单网络异常，{Attempt}/{Max} 次重试，{Delay}ms 后重试（幂等ID={ClientOrderId}）: {Symbol} {Side}",
                        attempt, maxRetries, delayMs, clientOrderId, instrumentSymbol, side);
                    await Task.Delay(delayMs, ct);
                }
            }

            if (response == null)
                throw new InvalidOperationException(
                    $"交易所下单响应为空（已重试 {maxRetries} 次）", lastNetworkException);

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

            await _dataService.SaveTradeRecordAsync(record, ct);

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
    /// 合约默认杠杆倍数。开仓前自动设置，避免使用交易所默认的 20x 导致强平风险过高。
    /// </summary>
    private const int DefaultFuturesLeverage = 10;

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
