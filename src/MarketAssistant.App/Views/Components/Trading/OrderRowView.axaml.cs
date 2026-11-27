using Avalonia.Controls;

namespace MarketAssistant.Views.Components.Trading;

/// <summary>
/// 未完成订单行。DataContext 为 <see cref="MarketAssistant.Trading.Abstractions.ExchangeOrderResult"/>，
/// 展示买卖方向、订单类型、关键价格（委托价 / 触发价）与成交进度。
/// </summary>
public partial class OrderRowView : UserControl
{
    public OrderRowView()
    {
        InitializeComponent();
    }
}
