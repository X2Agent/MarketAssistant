using System.Collections.Concurrent;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 待完成订单状态同步服务：查询交易所最新状态并回写本地订单、持仓与日统计。
/// 顺带以固定周期刷新账户快照，保证回撤熔断的峰值数据在监控期间保持新鲜。
/// </summary>
public sealed class OrderStateSyncService(
    [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
    TradingDataService dataService,
    CryptoPortfolioService portfolioService,
    IAlertCenterService alertCenterService,
    ILogger<OrderStateSyncService> logger)
{
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(10);
    private const int MaxConcurrentSymbolQueries = 5;
    private static readonly TimeSpan SnapshotRefreshInterval = TimeSpan.FromMinutes(2);

    private readonly IExchangeClient _exchangeClient = exchangeClient;
    private readonly TradingDataService _dataService = dataService;
    private readonly CryptoPortfolioService _portfolioService = portfolioService;
    private readonly IAlertCenterService _alertCenterService = alertCenterService;
    private readonly ILogger<OrderStateSyncService> _logger = logger;

    /// <summary>
    /// 交易记录转为成交终态（Filled）时触发，携带最新记录。
    /// 供策略层完结一次性保护策略（如交易所侧条件单成交后的自动完结）；
    /// 本地持仓 FIFO 与日统计已由 <see cref="TradingDataService.ReconcileTradeRecordAsync"/> 回写。
    /// </summary>
    public event Action<TradeRecord>? TradeRecordFilled;
    private readonly ConcurrentDictionary<string, DateTime> _lastSyncAt =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _symbolQueryGate = new(MaxConcurrentSymbolQueries, MaxConcurrentSymbolQueries);
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private DateTime _lastSnapshotAt = DateTime.MinValue;

    public async Task<int> SyncPendingOrdersAsync(
        string? symbol = null,
        bool force = false,
        CancellationToken ct = default)
    {
        var syncKey = string.IsNullOrWhiteSpace(symbol) ? "*" : symbol.Trim().ToUpperInvariant();
        if (!force && !ShouldSync(syncKey))
            return 0;

        if (!await _syncGate.WaitAsync(0, ct).ConfigureAwait(false))
            return 0;

        try
        {
            await RefreshAccountSnapshotIfDueAsync(ct).ConfigureAwait(false);

            var records = await _dataService.GetUnsettledTradeRecordsAsync(symbol, ct).ConfigureAwait(false);

            // 按交易对分组：组内串行查询同一交易对的多个订单，组间并行查询不同交易对
            var symbolGroups = records
                .GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var syncTasks = new Task<int>[symbolGroups.Length];
            for (var i = 0; i < symbolGroups.Length; i++)
                syncTasks[i] = SyncSymbolGroupAsync(symbolGroups[i], ct);

            var counts = await Task.WhenAll(syncTasks).ConfigureAwait(false);
            var updatedCount = counts.Sum();

            _lastSyncAt[syncKey] = DateTime.UtcNow;
            return updatedCount;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    /// <summary>
    /// 同步单个交易对下的全部待完成订单。通过 <see cref="_symbolQueryGate"/> 限制并发交易对数量，
    /// 单个交易对查询失败不影响其它交易对。
    /// </summary>
    private async Task<int> SyncSymbolGroupAsync(
        IGrouping<string, TradeRecord> symbolGroup,
        CancellationToken ct)
    {
        var updatedCount = 0;
        try
        {
            await _symbolQueryGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var record in symbolGroup)
                {
                    var previousStatus = record.Status;
                    var previousQty = record.ExecutedQty;
                    var previousCompletedAt = record.CompletedAt;

                    try
                    {
                        var latest = await _exchangeClient.GetOrderAsync(
                            record.Symbol,
                            record.ExchangeOrderId.ToString(),
                            ct).ConfigureAwait(false);

                        await _dataService.ReconcileTradeRecordAsync(record, latest, ct).ConfigureAwait(false);

                        if (record.Status != previousStatus
                            || record.ExecutedQty != previousQty
                            || record.CompletedAt != previousCompletedAt)
                        {
                            updatedCount++;
                        }

                        // 首次到达成交终态：通知策略层完结保护策略（条件单成交自动离场）
                        if (previousStatus != TradeRecordStatus.Filled && record.Status == TradeRecordStatus.Filled)
                            TradeRecordFilled?.Invoke(record);

                        // 首次进入部分成交：提示用户剩余数量仍在挂单中，并在超时后撤掉余量。
                        // 撤单是必要的：策略已经认为"已触发"，若余量长期挂着，
                        // 仓位会在策略不知情的情况下继续变化（后续止损数量随之失真）。
                        if (previousStatus != TradeRecordStatus.PartiallyFilled
                            && record.Status == TradeRecordStatus.PartiallyFilled)
                        {
                            await NotifyPartialFillAsync(record, ct).ConfigureAwait(false);
                        }

                        // 部分成交且已超过限价单超时时间：撤掉余量，把实际成交量作为最终结果。
                        // 仅处理策略限价/挂单单：手动单与紧急平仓单由用户自己决策
                        //（与 PendingOrderTimeoutService 的口径一致），
                        // 条件单腿不经过此路径撤单（腿的保护语义必须常驻）
                        if (record.Status == TradeRecordStatus.PartiallyFilled
                            && PendingOrderTimeoutService.IsStrategyLimitOrder(record))
                        {
                            await CancelRemainingIfExpiredAsync(record, ct).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex) when (ex is FriendlyException or HttpRequestException)
                    {
                        _logger.LogWarning(ex,
                            "同步订单状态失败，稍后重试: {Symbol} {OrderId}",
                            record.Symbol,
                            record.ExchangeOrderId);
                    }
                }
            }
            finally
            {
                _symbolQueryGate.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 单个交易对整体失败时记录错误并继续处理其它交易对
            _logger.LogError(ex,
                "同步交易对 {Symbol} 订单状态失败，跳过该交易对",
                symbolGroup.Key);
        }

        return updatedCount;
    }

    /// <summary>
    /// 部分成交告警：剩余数量仍在交易所挂单中，用户需要知道"触发≠完成"。
    /// 告警失败仅记录，不影响订单同步主链。
    /// </summary>
    private async Task NotifyPartialFillAsync(TradeRecord record, CancellationToken ct)
    {
        try
        {
            var remaining = record.RequestedQty - record.ExecutedQty;
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = record.Symbol,
                Level = AlertLevel.Warning,
                Source = AlertSource.System,
                Title = "订单部分成交",
                Content = $"{record.Symbol} 订单已成交 {record.ExecutedQty}，剩余 {remaining} 仍在挂单中等待成交"
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "上报部分成交告警失败: {OrderId}", record.ExchangeOrderId);
        }
    }

    /// <summary>
    /// 部分成交超时撤余量。
    /// 判定依据：记录创建已超过 <see cref="RiskConfig.LimitOrderTimeoutSeconds"/>（0 表示不自动撤），
    /// 且仍处于部分成交状态。调用方已按 <see cref="PendingOrderTimeoutService.IsStrategyLimitOrder"/>
    /// 过滤（仅策略限价/挂单单）。撤单成功与否都不阻断同步主链——撤单失败下轮重试。
    /// 撤单后不修改记录状态（等待交易所最终状态回填 CANCELED 或后续成交），
    /// 只发一条告警让用户知道"本次实际只成交了这么多"。
    /// </summary>
    private async Task CancelRemainingIfExpiredAsync(TradeRecord record, CancellationToken ct)
    {
        if (record.ExchangeOrderId <= 0)
            return;

        RiskConfig riskConfig;
        try
        {
            riskConfig = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "读取风控配置失败，跳过部分成交撤余量: {OrderId}", record.ExchangeOrderId);
            return;
        }

        if (riskConfig.LimitOrderTimeoutSeconds <= 0)
            return;

        var age = DateTime.UtcNow - record.CreatedAt;
        if (age < TimeSpan.FromSeconds(riskConfig.LimitOrderTimeoutSeconds))
            return;

        try
        {
            await _exchangeClient.CancelOrderAsync(record.Symbol, record.ExchangeOrderId.ToString(), ct)
                .ConfigureAwait(false);

            var remaining = record.RequestedQty - record.ExecutedQty;
            _logger.LogWarning(
                "部分成交超时已撤余量: {Symbol} {OrderId} 已成交 {ExecutedQty} / 请求 {RequestedQty}，撤掉剩余 {Remaining}",
                record.Symbol, record.ExchangeOrderId, record.ExecutedQty, record.RequestedQty, remaining);

            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = record.Symbol,
                Level = AlertLevel.Info,
                Source = AlertSource.System,
                Title = "部分成交超时已撤余量",
                Content = $"{record.Symbol} 订单超时未完全成交，已成交 {record.ExecutedQty}，"
                          + $"剩余 {remaining} 已撤销"
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "部分成交撤余量失败，下轮重试: {Symbol} {OrderId}", record.Symbol, record.ExchangeOrderId);
        }
    }

    private bool ShouldSync(string syncKey)
    {
        if (!_lastSyncAt.TryGetValue(syncKey, out var lastSync))
            return true;

        return DateTime.UtcNow - lastSync >= AutoSyncInterval;
    }

    /// <summary>
    /// 周期刷新账户快照，供回撤熔断使用。
    /// 风控仅在每笔交易通过时刷新快照，长期不交易时峰值数据会失真；
    /// 此处借助订单同步周期（约 2 分钟一次）保持快照新鲜，失败不影响订单同步本身。
    /// </summary>
    private async Task RefreshAccountSnapshotIfDueAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastSnapshotAt < SnapshotRefreshInterval)
            return;

        if (!await _snapshotGate.WaitAsync(0, ct).ConfigureAwait(false))
            return;

        try
        {
            var summary = await _portfolioService.GetAccountBalanceSummaryAsync(ct).ConfigureAwait(false);
            if (summary.TotalValueUSDT > 0)
                await _dataService.SaveAccountSnapshotAsync(summary.TotalValueUSDT, ct).ConfigureAwait(false);
            _lastSnapshotAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "周期刷新账户快照失败，回撤熔断数据可能滞后");
        }
        finally
        {
            _snapshotGate.Release();
        }
    }
}