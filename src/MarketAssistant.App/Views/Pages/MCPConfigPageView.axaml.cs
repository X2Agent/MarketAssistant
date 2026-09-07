using Avalonia.Controls;

namespace MarketAssistant.Views.Pages;

/// <summary>
/// MCP 服务器配置页。
/// 列表选择、键盘导航由 ListBox 内建处理，页面仅承载视图结构。
/// </summary>
public partial class MCPConfigPageView : UserControl
{
    public MCPConfigPageView()
    {
        InitializeComponent();
    }
}
