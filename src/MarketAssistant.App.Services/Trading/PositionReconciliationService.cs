using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 持仓对账：以交易所持仓为真相，校准本地 FIFO 账本。
///
/// 本地账本（positions 表）是 DCA 均价、已实现盈亏与风控"可卖数量"的计算基础，
/// 但它只由本应用自己的成交写入。以下场景会让它与交易所永久漂移：
/// 用户在币安 App 手动平仓、漏单、断线期间的成交、条件单在应用关闭时成交。
/// 一旦漂移，风控会按虚高的持仓放行超卖，PnL 与绩效统计也随之失真。
///
/// 与下单串行化：修正写入与交易的"落库持仓"走同一把标的执行锁
///（<see cref="TradeExecutor.GetExecutionGate"/>）。批量对比发现漂移后，
/// 在锁内重新拉取该标的的交易所持仓与本地账本再判定——否则"对账读到旧持仓快照 →
/// 交易成交落库 → 对账把成交覆盖回旧值"的竞态会让新成交被误清。
///
/// 判定容差：相对偏差超过 <see cref="RelativeTolerance"/> 或绝对偏差超过
/// <see cref="AbsoluteTolerance"/> 才修正，避免交易所小数精度与四舍五入
/// 造成每次对账都"发现差异"并反复写库。
/// </summary>
public sealed class PositionReconciliationService(
    [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
    TradingDataService dataService,
    TradeExecutor tradeExecutor,
    IAlertCenterService alertCenterService,
    ILogger<PositionReconciliationService> logger)
{
    /// <summary>相对偏差容差（0.5%）。</summary>
    private const decimal RelativeTolerance = 0.005m;

    /// <summary>绝对偏差容差：小于该数量视为精度差异，不做修正。</summary>
    private const decimal AbsoluteTolerance = 0.00000001m;

    private readonly IExchangeClient _exchangeClient = exchangeClient;
    private readonly TradingDataService _dataService = dataService;
    private readonly TradeExecutor _tradeExecutor = tradeExecutor;
    private readonly IAlertCenterService _alertCenterService = alertCenterService;
    private readonly ILogger<PositionReconciliationService> _logger = logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 执行一次对账。返回被修正的标的数量；同一时刻已有对账进行时返回 0。
    /// </summary>
    public async Task<int> ReconcileAsync(CancellationToken ct = default)
    {
        // 同一时刻只允许一次对账：并发修正同一标的多行会互相覆盖
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
            return 0;

        try
        {
            return await ReconcileCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> ReconcileCoreAsync(CancellationToken ct)
    {
        var localPositions = await _dataService.GetOpenPositionsAsync(null, ct).ConfigureAwait(false);
        var exchangeClient = _exchangeClient;

        // 现货：交易所没有"持仓"概念，用账户余额（扣掉 USDT 计价资产）逐币种对比
        if (!exchangeClient.IsFutures)
            return await ReconcileSpotAsync(localPositions, ct).ConfigureAwait(false);

        // 第一阶段（批量对比）：全量拉取两侧数据，只找出漂移的 (标的, 方向) 键
        var exchangePositions = await exchangeClient.GetPositionsAsync(null, ct).ConfigureAwait(false);
        var drifted = CollectDriftedFuturesKeys(localPositions, exchangePositions);
        var corrected = 0;

        // 第二阶段（逐键修正）：批量读与写之间可能夹着新成交，锁内用最新数据复核，
        // 批量阶段发现的"漂移"很多会在复核时自然消失
        foreach (var key in drifted)
        {
            ct.ThrowIfCancellationRequested();

            var side = key.IsLong ? PositionSide.Long : PositionSide.Short;
            string message;

            var symbolGate = _tradeExecutor.GetExecutionGate(key.Symbol);
            await symbolGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 锁内复核：与交易的持仓落库互斥，读到的一定是交易前后的稳定状态
                if ((await _dataService.GetUnsettledTradeRecordsAsync(key.Symbol, ct)).Count != 0) continue;
                var freshExchange = await exchangeClient.GetPositionsAsync(key.Symbol, ct)
                    .ConfigureAwait(false);
                var freshLocal = await _dataService.GetOpenPositionsAsync(key.Symbol, ct)
                    .ConfigureAwait(false);

                var freshExchangeQty = freshExchange
                    .Where(p => p.PositionAmt != 0 && (p.PositionAmt > 0) == key.IsLong)
                    .Sum(p => Math.Abs(p.PositionAmt));
                var freshLocalQty = freshLocal
                    .Where(p => p.Side == side)
                    .Sum(p => p.RemainingQuantity);
                if (IsWithinTolerance(freshLocalQty, freshExchangeQty))
                    continue;

                var entryPrice = ResolveEntryPrice(freshExchange.Where(p => p.PositionAmt != 0 && (p.PositionAmt > 0) == key.IsLong));
                await _dataService
                    .ReconcileOpenPositionAsync(key.Symbol, side, freshExchangeQty, entryPrice, ct)
                    .ConfigureAwait(false);

                corrected++;
                _logger.LogWarning(
                    "持仓已按交易所校准: {Symbol} {Side} 本地 {LocalQty} → 交易所 {ExchangeQty}（均价 {EntryPrice}）",
                    key.Symbol, side, freshLocalQty, freshExchangeQty, entryPrice);

                message = $"{key.Symbol} {(key.IsLong ? "多" : "空")}头持仓已按交易所校准：本地 {freshLocalQty:F8} → 交易所 {freshExchangeQty:F8}";
            }
            finally
            {
                symbolGate.Release();
            }

            await RaiseNoticeAsync(key.Symbol, message, ct).ConfigureAwait(false);
        }

        return corrected;
    }

    /// <summary>
    /// 批量对比合约持仓，返回漂移的 (标的, 方向) 键（不做任何写操作）。
    /// </summary>
    private static List<(string Symbol, bool IsLong)> CollectDriftedFuturesKeys(
        List<Position> localPositions, List<ExchangePosition> exchangePositions)
    {
        var exchangeByKey = exchangePositions
            .Where(p => p.PositionAmt != 0)
            .GroupBy(p => (Symbol: p.Symbol.ToUpperInvariant(), IsLong: p.PositionAmt > 0))
            .ToDictionary(
                g => g.Key,
                g => new ExchangePositionSnapshot(
                    g.Sum(p => Math.Abs(p.PositionAmt)),
                    ResolveEntryPrice(g)));

        var localByKey = localPositions
            .GroupBy(p => (Symbol: p.Symbol.ToUpperInvariant(), IsLong: p.Side == PositionSide.Long))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.RemainingQuantity));

        var driftedKeys = new List<(string, bool)>();
        foreach (var key in exchangeByKey.Keys.Union(localByKey.Keys))
        {
            exchangeByKey.TryGetValue(key, out var snapshot);
            var exchangeQty = snapshot?.Quantity ?? 0m;
            localByKey.TryGetValue(key, out var localQty);
            if (!IsWithinTolerance(localQty, exchangeQty))
                driftedKeys.Add(key);
        }

        return driftedKeys;
    }

    /// <summary>
    /// 现货对账：交易所余额即真相。只对比本地有记录或有余额的币种。
    /// 现货余额包含用户非本应用买入的资产，因此只修正"本地有记录但交易所变少"的方向，
    /// 不因交易所有余额而凭空造出策略持仓。
    /// </summary>
    private async Task<int> ReconcileSpotAsync(List<Position> localPositions, CancellationToken ct)
    {
        if (localPositions.Count == 0)
            return 0;

        var accountInfo = await _exchangeClient.GetAccountInfoAsync(ct).ConfigureAwait(false);
        var balances = accountInfo.Balances
            .ToDictionary(b => b.Asset.ToUpperInvariant(), b => b.Free + b.Locked);

        // 第一阶段（批量对比）：找"交易所余额比本地账本少"的漂移标的
        var driftedSymbols = new List<(string Symbol, decimal LocalQty, string BaseAsset)>();
        foreach (var group in localPositions.GroupBy(p => CryptoSymbolConverter.ExtractBaseCurrency(p.Symbol)))
        {
            var localQty = group.Sum(p => p.RemainingQuantity);
            if (localQty <= 0)
                continue;

            // 复用统一的计价后缀解析；未匹配到已知后缀时原样返回，与输入相同视为无法映射余额，跳过
            var baseAsset = group.Key;
            var symbol = group.First().Symbol;
            if (string.Equals(baseAsset, symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            var exchangeQty = balances.TryGetValue(baseAsset, out var balance) ? balance : 0m;

            // 现货只处理"交易所比本地少"（外部卖出/条件单成交），
            // 交易所比本地多可能是用户自己买的，不能算进策略仓位
            if (exchangeQty >= localQty || IsWithinTolerance(localQty, exchangeQty))
                continue;

            if (group.Any(p => p.Symbol != symbol))
            {
                await RaiseNoticeAsync(symbol, $"{baseAsset} 跨交易对库存不足：账本 {localQty}，余额 {exchangeQty}。无法归属差额，请核对成交。", ct);
                continue;
            }
            driftedSymbols.Add((symbol, localQty, baseAsset));
        }

        var corrected = 0;
        foreach (var (symbol, localQty, baseAsset) in driftedSymbols)
        {
            ct.ThrowIfCancellationRequested();

            string message;
            var symbolGate = _tradeExecutor.GetExecutionGate(symbol);
            await symbolGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 锁内复核：批量读与写之间可能有新成交改变两侧数量
                if ((await _dataService.GetUnsettledTradeRecordsAsync(symbol, ct)).Count != 0) continue;
                var freshAccount = await _exchangeClient.GetAccountInfoAsync(ct).ConfigureAwait(false);
                var freshBalance = freshAccount.Balances
                    .FirstOrDefault(b => b.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase));
                var freshExchangeQty = freshBalance == null ? 0m : freshBalance.Free + freshBalance.Locked;

                var freshLocal = await _dataService.GetOpenPositionsAsync(symbol, ct).ConfigureAwait(false);
                var freshLocalQty = freshLocal.Sum(p => p.RemainingQuantity);

                if (freshExchangeQty >= freshLocalQty || IsWithinTolerance(freshLocalQty, freshExchangeQty))
                    continue;

                await _dataService
                    .ReconcileOpenPositionAsync(symbol, PositionSide.Long, freshExchangeQty, 0m, ct)
                    .ConfigureAwait(false);

                corrected++;
                _logger.LogWarning(
                    "现货持仓已按交易所余额校准: {Symbol} 本地 {LocalQty} → 余额 {ExchangeQty}",
                    symbol, freshLocalQty, freshExchangeQty);

                message = $"{symbol} 现货持仓已按交易所余额校准：本地 {freshLocalQty:F8} → 交易所 {freshExchangeQty:F8}";
            }
            finally
            {
                symbolGate.Release();
            }

            await RaiseNoticeAsync(symbol, message, ct).ConfigureAwait(false);
        }

        return corrected;
    }

    /// <summary>偏差是否在容差内（绝对或相对）。</summary>
    private static bool IsWithinTolerance(decimal local, decimal exchange)
    {
        var diff = Math.Abs(local - exchange);
        if (diff <= AbsoluteTolerance)
            return true;

        var reference = Math.Max(Math.Abs(local), Math.Abs(exchange));
        return reference > 0 && diff / reference <= RelativeTolerance;
    }

    /// <summary>多行同方向持仓的数量加权均价（用作交易所均价缺失时的兜底）。</summary>
    private static decimal ResolveEntryPrice(IEnumerable<ExchangePosition> positions)
    {
        var priced = positions.Where(p => p.EntryPrice > 0).ToList();
        var totalQty = priced.Sum(p => Math.Abs(p.PositionAmt));
        return totalQty > 0 ? priced.Sum(p => p.EntryPrice * Math.Abs(p.PositionAmt)) / totalQty : 0m;
    }

    private async Task RaiseNoticeAsync(string symbol, string message, CancellationToken ct)
    {
        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = symbol,
                Level = AlertLevel.Warning,
                Source = AlertSource.System,
                Title = "持仓已按交易所校准",
                Content = message
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 告警失败不影响已完成的对账
            _logger.LogWarning(ex, "上报持仓校准告警失败: {Symbol}", symbol);
        }
    }

    private sealed record ExchangePositionSnapshot(decimal Quantity, decimal EntryPrice);
}
