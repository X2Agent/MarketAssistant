using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 策略限价单超时管理：挂起超过 <see cref="RiskConfig.LimitOrderTimeoutSeconds"/> 仍未成交的
/// 策略限价单自动撤单并告警。没有这一步，限价单会无限期挂在交易所，
/// 用户以为"已触发"而实际上仓位从未建立（或从未退出）。
/// </summary>
/// <remarks>
/// 仅处理策略订单（strategyId 非 "manual"/紧急平仓）：用户手动挂的限价单由用户自行管理，
/// 应用代为撤单会造成"我的单子怎么没了"的困惑。
/// 撤单后策略的触发计数不回滚——本次触发已消耗，是否重试由用户在策略页决定。
/// </remarks>
public sealed class PendingOrderTimeoutService : IDisposable
{
    /// <summary>扫描周期。</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

    private readonly IExchangeClient _exchangeClient;
    private readonly TradingDataService _dataService;
    private readonly IAlertCenterService _alertCenterService;
    private readonly ILogger<PendingOrderTimeoutService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;

    public PendingOrderTimeoutService(
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        TradingDataService dataService,
        IAlertCenterService alertCenterService,
        ILogger<PendingOrderTimeoutService> logger)
    {
        _exchangeClient = exchangeClient;
        _dataService = dataService;
        _alertCenterService = alertCenterService;
        _logger = logger;
        _loopTask = Task.Run(() => ScanLoopAsync(_cts.Token));
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await ScanAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "限价单超时扫描异常");
            }
        }
    }

    /// <summary>扫描一次超时未成交的策略限价单（internal 供单测直接驱动）。</summary>
    internal async Task<int> ScanAsync(CancellationToken ct = default)
    {
        var config = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);
        if (config.LimitOrderTimeoutSeconds <= 0)
            return 0;

        var deadline = DateTime.UtcNow.AddSeconds(-config.LimitOrderTimeoutSeconds);
        var records = await _dataService.GetUnsettledTradeRecordsAsync(null, ct).ConfigureAwait(false);

        var cancelledCount = 0;
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsStrategyLimitOrder(record) || record.CreatedAt > deadline)
                continue;

            try
            {
                var latest = await _exchangeClient
                    .GetOrderAsync(record.Symbol, record.ExchangeOrderId.ToString(), ct)
                    .ConfigureAwait(false);

                // 已成交/已取消的订单交给既有对账管线，本服务只处理"仍挂着"的限价单
                if (!IsStillWorking(latest.Status))
                    continue;

                var cancelResponse = await _exchangeClient
                    .CancelOrderAsync(record.Symbol, record.ExchangeOrderId.ToString(), ct)
                    .ConfigureAwait(false);

                await _dataService
                    .ReconcileTradeRecordAsync(record, cancelResponse, ct)
                    .ConfigureAwait(false);

                cancelledCount++;
                _logger.LogInformation(
                    "策略限价单超时未成交，已撤单: {StrategyId} {Symbol} 订单 {OrderId} 挂起时长 {Minutes:F1} 分钟",
                    record.StrategyId, record.Symbol, record.ExchangeOrderId,
                    (DateTime.UtcNow - record.CreatedAt).TotalMinutes);

                await _alertCenterService.RaiseAlertAsync(new AlertEvent
                {
                    MarketType = MarketType.Crypto,
                    Symbol = record.Symbol,
                    Level = AlertLevel.Warning,
                    Source = AlertSource.System,
                    Title = "限价单超时已撤单",
                    Content = $"{record.Symbol} 的策略限价单挂起超过 {config.LimitOrderTimeoutSeconds} 秒未成交，已撤销。"
                              + "该次策略触发已消耗，如需成交请重新启用策略"
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 单个订单处理失败不影响其余订单；下个扫描周期自然重试
                _logger.LogWarning(ex, "处理超时限价单失败: {OrderId}", record.ExchangeOrderId);
            }
        }

        return cancelledCount;
    }

    /// <summary>是否策略下限价单（排除手动单与紧急平仓单）。</summary>
    private static bool IsStrategyLimitOrder(TradeRecord record) =>
        record.OrderType == OrderType.Limit
        && !string.IsNullOrEmpty(record.StrategyId)
        && !record.StrategyId.Equals("manual", StringComparison.OrdinalIgnoreCase)
        && !record.StrategyId.Equals(EmergencyStopService.EmergencyCloseStrategyId, StringComparison.OrdinalIgnoreCase);

    /// <summary>订单是否仍在挂单状态（NEW / PARTIALLY_FILLED 均可能继续成交）。</summary>
    private static bool IsStillWorking(string status) =>
        status is "NEW" or "PARTIALLY_FILLED";

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "限价单超时服务停止异常（已忽略）");
        }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
