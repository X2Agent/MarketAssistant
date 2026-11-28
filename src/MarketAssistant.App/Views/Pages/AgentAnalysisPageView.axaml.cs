using Avalonia.Controls;
using Avalonia.Input;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

/// <summary>
/// 代理分析页面视图
/// </summary>
public partial class AgentAnalysisPageView : UserControl
{
    public AgentAnalysisPageView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is AgentAnalysisViewModel vm)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                vm.SetStorageProvider(topLevel?.StorageProvider);
            }
        };
    }

    /// <summary>
    /// 点击抽屉遮罩收起智能助手。遮罩只覆盖报告区，抽屉自身不在其内部，
    /// 故这里不需要判断点击目标是否为抽屉内容。
    /// </summary>
    private void OnChatDrawerScrimTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AgentAnalysisViewModel vm && vm.ToggleChatSidebarCommand.CanExecute(null))
        {
            vm.ToggleChatSidebarCommand.Execute(null);
        }

        e.Handled = true;
    }
}
