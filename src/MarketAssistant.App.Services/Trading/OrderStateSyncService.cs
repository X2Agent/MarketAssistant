using System.Collections.Concurrent;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 待完成订单状态同步服务：查询交易所最新状态并回写本地订单、持仓与日统计。
/// </summary>
public sealed class OrderStateSyncService
{
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(10);

    private readonly IExchangeClient _exchangeClient;
    private readonly TradingDataService _dataService;
    private readonly ILogger<OrderStateSyncService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastSyncAt =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public OrderStateSyncService(
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        TradingDataService dataService,
        ILogger<OrderStateSyncService> logger)
    {
        _exchangeClient = exchangeClient;
        _dataService = dataService;
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
            var records = await _dataService.GetUnsettledTradeRecordsAsync(symbol, ct).ConfigureAwait(false);
            var updatedCount = 0;

            foreach (var record in records)
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

            _lastSyncAt[syncKey] = DateTime.UtcNow;
            return updatedCount;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private bool ShouldSync(string syncKey)
    {
        if (!_lastSyncAt.TryGetValue(syncKey, out var lastSync))
            return true;

        return DateTime.UtcNow - lastSync >= AutoSyncInterval;
    }
}