using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 应用级交易确认服务：以单例生命周期订阅 <see cref="TradeExecutor.ConfirmationRequested"/>，
/// 通过 <see cref="IDialogService"/> 弹出全局确认对话框，使 Human-in-the-Loop 确认
/// 不再依赖交易监控页面的存活（此前仅监控页 VM 订阅，导航离开即退订，
/// 所有超阈值订单会被静默拒绝）。
/// 对话框 60 秒无操作自动拒绝；无法获取活动窗口（如最小化到托盘）时同样拒绝并弹通知提醒。
/// </summary>
public sealed class TradeConfirmationService : IDisposable
{
    private const int ConfirmationTimeoutSeconds = 60;

    private readonly TradeExecutor _tradeExecutor;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TradeConfirmationService> _logger;

    private int _pendingConfirmationCount;

    public TradeConfirmationService(
        TradeExecutor tradeExecutor,
        IDialogService dialogService,
        INotificationService notificationService,
        ILogger<TradeConfirmationService> logger)
    {
        _tradeExecutor = tradeExecutor;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _logger = logger;
        _tradeExecutor.ConfirmationRequested += OnConfirmationRequestedAsync;
    }

    /// <summary>
    /// 是否有确认请求正在进行（同一时刻只允许一个对话框，后续请求直接拒绝，
    /// 避免多个待确认交易叠加导致用户误批）。
    /// </summary>
    private bool HasPendingConfirmation => Volatile.Read(ref _pendingConfirmationCount) > 0;

    private async Task<bool> OnConfirmationRequestedAsync(
        string symbol, OrderSide side, decimal price, decimal quantity, string reason)
    {
        // 单确认串行化：已有对话框挂起时拒绝新请求（fail-closed，宁可错过不可误做）
        if (Interlocked.CompareExchange(ref _pendingConfirmationCount, 1, 0) != 0)
        {
            _logger.LogWarning("已有交易确认等待中，拒绝新确认请求: {Symbol} {Side}", symbol, side);
            return false;
        }

        try
        {
            var title = "自动交易确认";
            var message =
                $"交易对：{symbol}\n方向：{side}\n价格：{price:F2}\n数量：{quantity}\n\n" +
                $"触发原因：{reason}\n\n（{ConfirmationTimeoutSeconds} 秒内未操作将自动拒绝）";

            // 超时通过取消令牌主动关闭模态对话框：仅 Task.WhenAny 竞争会让对话框
            // 留在屏幕上，用户随后点击"批准"的结果会被丢弃，误以为交易已批准
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ConfirmationTimeoutSeconds));
            var approved = await _dialogService
                .ShowConfirmationAsync(title, message, "批准", "拒绝", timeoutCts.Token)
                .ConfigureAwait(false);

            if (approved)
            {
                _logger.LogInformation("用户批准自动交易: {Symbol} {Side}", symbol, side);
            }
            else
            {
                // 超时/窗口不可用/用户拒绝均走此分支；ShowCustomDialogAsync 拿不到活动窗口时返回 null（视为拒绝）
                if (timeoutCts.IsCancellationRequested)
                {
                    _logger.LogWarning("交易确认超时自动拒绝: {Symbol} {Side}", symbol, side);
                    _notificationService.ShowWarning(
                        $"⚠ 交易确认超时已自动拒绝：{symbol} {side} {quantity}");
                }
                else
                {
                    _logger.LogWarning("交易确认被拒绝或窗口不可用: {Symbol} {Side}", symbol, side);
                    _notificationService.ShowWarning($"已拒绝自动交易：{symbol} {side} {quantity}");
                }
            }

            return approved;
        }
        finally
        {
            Volatile.Write(ref _pendingConfirmationCount, 0);
        }
    }

    public void Dispose()
    {
        _tradeExecutor.ConfirmationRequested -= OnConfirmationRequestedAsync;
        GC.SuppressFinalize(this);
    }
}
