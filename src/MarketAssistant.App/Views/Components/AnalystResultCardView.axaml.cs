using Avalonia;
using Avalonia.Controls;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 分析师结构化结果卡片：评级徽标 + 评分条 + 核心观点 + 关键数据 + 风险提示 + 可展开原始文本。
/// 数据源为 <see cref="AnalystCardViewModel"/>，由 ChatMessageAdapter 解析分析师 JSON 产物构建。
/// </summary>
public partial class AnalystResultCardView : UserControl
{
    public static readonly StyledProperty<AnalystCardViewModel?> CardProperty =
        AvaloniaProperty.Register<AnalystResultCardView, AnalystCardViewModel?>(nameof(Card));

    public AnalystCardViewModel? Card
    {
        get => GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    public AnalystResultCardView()
    {
        InitializeComponent();
    }
}