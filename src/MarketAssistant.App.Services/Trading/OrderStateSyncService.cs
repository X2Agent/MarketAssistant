using System.Collections.Concurrent;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 待完成订单状态同步服务：查询交易所最新状态并回写本地订单、持仓与日统计。
/// 顺带以固定周期刷新账户快照，保证回撤熔断的峰值数据在监控期间保持新鲜。
/// </summary>
public sealed class OrderStateSyncService
{
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(10);
    private const int MaxConcurrentSymbolQueries = 5;
    private static readonly TimeSpan SnapshotRefreshInterval = TimeSpan.FromMinutes(2);

    private readonly IExchangeClient _exchangeClient;
    private readonly TradingDataService _dataService;
    private readonly CryptoPortfolioService _portfolioService;
    private readonly ILogger<OrderStateSyncService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastSyncAt =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _symbolQueryGate = new(MaxConcurrentSymbolQueries, MaxConcurrentSymbolQueries);
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private DateTime _lastSnapshotAt = DateTime.MinValue;

    public OrderStateSyncService(
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        TradingDataService dataService,
        CryptoPortfolioService portfolioService,
        ILogger<OrderStateSyncService> logger)
    {
        _exchangeClient = exchangeClient;
        _dataService = dataService;
        _portfolioService = portfolioService;
        _logger = logger;
    }

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