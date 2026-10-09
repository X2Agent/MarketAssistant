using MarketAssistant.ViewModels.Home;

namespace MarketAssistant.Services.Dialog;

/// <summary>
/// 非模态窗口服务：管理与主界面并存的应用级窗口（区别于 <see cref="IDialogService"/> 的模态对话框）。
/// 统一处理 owner 选择（主窗口可能隐藏进托盘）与重复打开防护，ViewModel 不直接创建窗口。
/// </summary>
public interface IWindowService
{
    /// <summary>
    /// 显示每日简报窗口（以 <paramref name="viewModel"/> 为 DataContext）。
    /// 已打开时聚焦既有窗口，避免重复开窗。
    /// </summary>
    void ShowDailyBriefingWindow(DailyBriefingViewModel viewModel);
}
