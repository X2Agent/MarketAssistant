using Avalonia;
using Avalonia.Controls;

namespace MarketAssistant.Views.Components.Trading;

/// <summary>
/// 策略行（只读展示 + 可注入操作按钮）。
/// 策略配置页、实时监控页与策略执行历史窗口共用，避免同一份"符号 + 类型标签 + 参数明细"标记多处拷贝。
/// DataContext 为 <see cref="Trading.Models.TradingStrategy"/>；操作按钮经 <see cref="Actions"/>
/// 注入，注入内容仍处于宿主 ItemsControl 的子树内，故可用 $parent[ItemsControl] 取宿主命令。
/// </summary>
public partial class StrategyRowView : UserControl
{
    /// <summary>行尾操作区内容；为空时不占位。</summary>
    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<StrategyRowView, object?>(nameof(Actions));

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public StrategyRowView()
    {
        InitializeComponent();
    }
}
