using Avalonia.Controls;

namespace MarketAssistant.Views.Components.Trading;

/// <summary>
/// 持仓行（合约与现货通用）。DataContext 为 <see cref="Trading.Models.PositionInfo"/>，
/// 展示方向、数量、开仓/标记/强平价、杠杆与未实现盈亏。
/// </summary>
public partial class PositionRowView : UserControl
{
    public PositionRowView()
    {
        InitializeComponent();
    }
}
