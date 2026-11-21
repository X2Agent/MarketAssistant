using Avalonia.Controls;
using Avalonia.VisualTree;

namespace MarketAssistant.Views.Pages;

public partial class PriceAlertPageView : UserControl
{
    public PriceAlertPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 资产框下拉关闭后，把被 Avalonia 全选的输入收回到末尾。
    /// 搜索无结果时由 ViewModel 收起下拉，用户此时仍在输入，全选会让下一个字符覆盖已键入内容；
    /// 选中候选项导致的关闭保持全选，便于直接重新输入。
    /// </summary>
    private void OnAssetBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is ComboBox { SelectedItem: null } box)
        {
            // 不用 ClearSelection()：它把光标收到选区起点（文本开头），后续输入会被插到最前面
            var input = box.FindDescendantOfType<TextBox>()!;
            input.SelectionStart = input.SelectionEnd;
        }
    }
}
