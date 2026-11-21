using Avalonia.Controls;
using Avalonia.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.ViewModels;

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

    /// <summary>
    /// 列表行内启用开关的点击转发：标记 Handled 阻止 ListBoxItem 选中（那会打开编辑表单），
    /// 再显式执行命令完成开关切换。
    /// </summary>
    private void OnServerTogglePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MCPServerConfig config } &&
            DataContext is MCPConfigPageViewModel viewModel)
        {
            e.Handled = true;
            viewModel.ToggleServerEnabledCommand.Execute(config);
        }
    }
}
