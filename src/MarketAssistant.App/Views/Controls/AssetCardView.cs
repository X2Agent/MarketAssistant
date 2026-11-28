using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace MarketAssistant.Views.Controls;

/// <summary>资产卡片的表面形态，决定底色与内边距；取值与 <see cref="SkeletonCardVariant"/> 的 Tile / Card 一一对应。</summary>
public enum AssetCardVariant
{
    /// <summary>瓦片：次级表面色 + 内边距 16，放在白色卡片内（首页热门资产）。</summary>
    Tile,

    /// <summary>卡片：卡片底色 + 内边距 20，直接放在页面底色上（自选看板）。</summary>
    Card,
}

/// <summary>
/// 资产卡片：首页热门资产与自选看板共用，保证同一类信息在两页的层级一致——
/// 名称与代码各占一行，价格独占一行作视觉锚点，涨跌用语义底色标签。
///
/// <para>
/// <see cref="ContentControl.Content"/> 是右上角操作位（收藏星标等），点击操作位不会冒泡成卡片激活，
/// 页面不需要再为每个操作按钮写一个"阻止冒泡"的处理器。
/// </para>
///
/// <para>
/// 骨架请用 <see cref="AssetCardSkeleton"/>：它是本控件的结构克隆，数据到达时零跳动。
/// </para>
/// </summary>
public class AssetCardView : ContentControl
{
    private static readonly string[] VariantClasses = ["variant-tile", "variant-card"];

    /// <summary>表面形态。</summary>
    public static readonly StyledProperty<AssetCardVariant> VariantProperty =
        AvaloniaProperty.Register<AssetCardView, AssetCardVariant>(nameof(Variant));

    /// <summary>资产名称。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(Title));

    /// <summary>资产代码。</summary>
    public static readonly StyledProperty<string?> CodeProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(Code));

    /// <summary>代码后的补充说明（如 A 股所属板块），为空时不显示分隔点。</summary>
    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(Subtitle));

    /// <summary>现价，已格式化的展示文本。</summary>
    public static readonly StyledProperty<string?> PriceProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(Price));

    /// <summary>涨跌幅文本（如 "+2.31%"），涨跌色由符号决定；为空时不显示标签。</summary>
    public static readonly StyledProperty<string?> ChangeTextProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(ChangeText));

    /// <summary>指标名（如 "净流入"），为空时整行指标不显示。</summary>
    public static readonly StyledProperty<string?> MetricLabelProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(MetricLabel));

    /// <summary>指标值，已格式化的展示文本。</summary>
    public static readonly StyledProperty<string?> MetricValueProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(MetricValue));

    /// <summary>名次文本（如 "#1"），为空时不显示名次徽标。</summary>
    public static readonly StyledProperty<string?> RankTextProperty =
        AvaloniaProperty.Register<AssetCardView, string?>(nameof(RankText));

    public AssetCardVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string? Price
    {
        get => GetValue(PriceProperty);
        set => SetValue(PriceProperty, value);
    }

    public string? ChangeText
    {
        get => GetValue(ChangeTextProperty);
        set => SetValue(ChangeTextProperty, value);
    }

    public string? MetricLabel
    {
        get => GetValue(MetricLabelProperty);
        set => SetValue(MetricLabelProperty, value);
    }

    public string? MetricValue
    {
        get => GetValue(MetricValueProperty);
        set => SetValue(MetricValueProperty, value);
    }

    public string? RankText
    {
        get => GetValue(RankTextProperty);
        set => SetValue(RankTextProperty, value);
    }

    static AssetCardView()
    {
        VariantProperty.Changed.AddClassHandler<AssetCardView>((card, _) => card.ApplyVariant());
        TitleProperty.Changed.AddClassHandler<AssetCardView>((card, change) =>
            AutomationProperties.SetName(card, change.NewValue as string));
    }

    public AssetCardView()
    {
        ApplyVariant();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 操作位的点击只属于操作本身，不能冒泡成"打开详情"
        e.NameScope.Get<Control>("PART_ActionHost").Tapped += (_, tapped) => tapped.Handled = true;
    }

    private void ApplyVariant()
    {
        // 用样式类而不是直接写 Background/Padding：底色必须跟随主题切换，
        // 内边距与骨架共用同一份令牌，改 Spacing.axaml 时两者一起变。
        foreach (var name in VariantClasses)
        {
            Classes.Remove(name);
        }

        Classes.Add(VariantClasses[(int)Variant]);
    }
}
