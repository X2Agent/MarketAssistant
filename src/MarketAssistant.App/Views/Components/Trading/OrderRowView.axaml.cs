using Avalonia.Controls;

namespace MarketAssistant.Views.Components.Trading;

/// <summary>
/// 未完成订单行。DataContext 为 <see cref="Trading.Abstractions.ExchangeOrderResult"/>，
/// 展示方向、委托/成交量与成交进度。
/// </summary>
public partial class OrderRowView : UserControl
{
    public OrderRowView()
    {
        InitializeComponent();
    }
}
