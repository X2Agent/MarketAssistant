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
    /// 点击遮罩层关闭侧边栏
    /// </summary>
    private void OnOverlayTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AgentAnalysisViewModel vm)
        {
            vm.ToggleChatSidebarCommand.Execute(null);
        }
    }
}
