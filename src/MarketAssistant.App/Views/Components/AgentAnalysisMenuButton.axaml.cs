using Avalonia.Controls;

namespace MarketAssistant.Views.Components;

/// <summary>
/// AI 分析页的「更多操作」菜单按钮。DataContext 为 <see cref="ViewModels.AgentAnalysisViewModel"/>，
/// 由主窗口返回条的操作槽按页面类型注入。
/// </summary>
public partial class AgentAnalysisMenuButton : UserControl
{
    public AgentAnalysisMenuButton()
    {
        InitializeComponent();
    }
}
