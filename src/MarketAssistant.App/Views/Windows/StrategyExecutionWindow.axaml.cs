using Avalonia.Controls;
using Avalonia.Interactivity;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Views.Windows;

/// <summary>
/// 策略执行历史对话框窗口。展示单个策略的概要与其关联的交易记录列表。
/// </summary>
public partial class StrategyExecutionWindow : Window
{
    public StrategyExecutionWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 设置窗口内容：策略概要（DataContext）+ 执行记录列表。
    /// </summary>
    public void SetContent(TradingStrategy strategy, IReadOnlyList<TradeRecord> records)
    {
        Title = $"策略执行历史 - {strategy.Symbol}";
        DataContext = strategy;
        RecordsItemsControl.ItemsSource = records;
        EmptyText.IsVisible = records.Count == 0;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
