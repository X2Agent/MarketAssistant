using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Trading;

namespace MarketAssistant.ViewModels.Trading;

/// <summary>
/// 交易监控页 - 紧急停止（kill switch）部分。
/// 两个独立按钮对应两种资金安全语义，均由用户显式触发并二次确认。
/// </summary>
public partial class TradeMonitorViewModel
{
    [ObservableProperty]
    private bool _isTradingHalted;

    [ObservableProperty]
    private string _haltReason = string.Empty;

    /// <summary>停机原因展示文本（未停机时为空）。</summary>
    public string HaltReasonText =>
        IsTradingHalted ? (string.IsNullOrWhiteSpace(HaltReason) ? "未注明原因" : HaltReason) : string.Empty;

    partial void OnIsTradingHaltedChanged(bool value)
        => OnPropertyChanged(nameof(HaltReasonText));

    partial void OnHaltReasonChanged(string value)
        => OnPropertyChanged(nameof(HaltReasonText));

    /// <summary>
    /// 停止全部策略并撤销本应用条件单（不平仓）。持仓保留，由用户自行决定去留。
    /// </summary>
    [RelayCommand]
    private async Task StopStrategiesAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "紧急停止",
            "将停止市场监控、暂停全部策略并撤销本应用挂出的条件单（持仓保留）。\n\n" +
            "停止后新订单会被拒绝，需要逐个重新启用策略。是否继续？",
            "确认停止",
            "取消",
            topmost: true);
        if (!confirmed)
            return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _emergencyStopService.StopStrategiesAndOrdersAsync("用户手动触发紧急停止");
            SyncHaltState();

            if (result.Success)
                _notificationService.ShowWarning(result.Message);
            else
                _notificationService.ShowError(result.Message);
        }, "紧急停止");
    }

    /// <summary>
    /// 停止策略并全部平仓（含合约 reduceOnly 与现货卖出）。市价成交，存在滑点风险。
    /// </summary>
    [RelayCommand]
    private async Task StopAndCloseAllAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "紧急停止并全部平仓",
            "将停止监控、暂停全部策略、撤销条件单，并以市价卖出全部现货持仓、" +
            "平掉全部合约持仓。\n\n市价成交存在滑点风险，且此操作不可撤销。是否继续？",
            "确认全部平仓",
            "取消",
            topmost: true);
        if (!confirmed)
            return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _emergencyStopService.CloseAllPositionsAsync("用户手动触发紧急停止并全部平仓");
            SyncHaltState();

            if (result.Success)
                _notificationService.ShowWarning(result.Message);
            else
                _notificationService.ShowError(result.Message);
        }, "紧急全部平仓");
    }

    /// <summary>
    /// 解除紧急停止。策略保持暂停——是否恢复自动交易由用户逐个显式启用。
    /// </summary>
    [RelayCommand]
    private async Task ResumeTradingAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "解除紧急停止",
            "解除后新订单不再被拒绝，但策略仍保持暂停，需要逐个重新启用。是否继续？",
            "解除停止",
            "取消");
        if (!confirmed)
            return;

        await SafeExecuteAsync(async () =>
        {
            await _emergencyStopService.ResumeAsync();
            SyncHaltState();
            _notificationService.ShowInfo("紧急停止已解除，策略仍为暂停状态");
        }, "解除紧急停止");
    }

    /// <summary>把停机状态同步到绑定属性（自动兼容后台线程与 UI 线程调用）。</summary>
    private void SyncHaltState()
    {
        var halted = _haltState.IsHalted;
        var reason = _haltState.HaltReason;

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            IsTradingHalted = halted;
            HaltReason = reason;
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsTradingHalted = halted;
            HaltReason = reason;
        });
    }
}
