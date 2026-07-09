using Avalonia.Controls;
using Avalonia.Interactivity;
using MarketAssistant.Infrastructure.Extensions;
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
    /// 设置窗口内容：策略概要 + 执行记录列表。
    /// </summary>
    public void SetContent(TradingStrategy strategy, IReadOnlyList<TradeRecord> records)
    {
        Title = $"策略执行历史 - {strategy.Symbol}";

        var symbolText = this.FindControl<TextBlock>("SymbolText");
        if (symbolText != null) symbolText.Text = strategy.Symbol;

        var typeText = this.FindControl<TextBlock>("TypeText");
        if (typeText != null) typeText.Text = strategy.Type.GetDescription();

        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText != null) statusText.Text = strategy.Status.GetDescription();

        var summaryText = this.FindControl<TextBlock>("SummaryText");
        if (summaryText != null)
        {
            summaryText.Text = $"方向: {strategy.Side.GetDescription()} | " +
                                $"触发价: {strategy.TriggerPrice:F4} | {strategy.QuantityLabel} | " +
                                $"已执行: {strategy.ExecutionCount}/{strategy.MaxExecutions} 次";
        }

        var emptyText = this.FindControl<TextBlock>("EmptyText");
        var recordsControl = this.FindControl<ItemsControl>("RecordsItemsControl");
        if (recordsControl != null)
        {
            recordsControl.ItemsSource = records;
            if (emptyText != null)
                emptyText.IsVisible = records.Count == 0;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
