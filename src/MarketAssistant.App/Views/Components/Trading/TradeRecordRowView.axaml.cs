using Avalonia.Controls;

namespace MarketAssistant.Views.Components.Trading;

/// <summary>
/// 交易记录行。DataContext 为 <see cref="Trading.Models.TradeRecord"/>，
/// 交易历史页与策略执行历史窗口共用，展示方向、类型、状态、已实现盈亏与成交明细。
/// </summary>
public partial class TradeRecordRowView : UserControl
{
    public TradeRecordRowView()
    {
        InitializeComponent();
    }
}
