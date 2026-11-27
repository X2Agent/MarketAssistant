using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MarketAssistant.Views.Controls;

/// <summary>骨架卡片的表面形态，取值与真实卡片的样式类一一对应。</summary>
public enum SkeletonCardVariant
{
    /// <summary>卡片：卡片底色 + 圆角 12 + 内边距 20（对应 <c>CardView</c> 与 <c>AssetCardView</c> 的 Card 形态）。</summary>
    Card,

    /// <summary>瓦片：次级表面色 + 圆角 12 + 内边距 16（对应 <c>AssetCardView</c> 的 Tile 形态）。</summary>
    Tile,

    /// <summary>行内卡：卡片底色 + 圆角 8 + 内边距 16（对应列表行、新闻行）。</summary>
    Row,

    /// <summary>无表面：只占位、不加底色（用于"骨架本身就是一条灰条"的场合）。</summary>
    Bare,
}

/// <summary>
/// 骨架卡片：<b>一个</b>骨架的壳 + 内容，与真实卡片一一对应。
///
/// <para>
/// 只管"一个骨架长什么样"，不负责重复——重复用 <see cref="SkeletonList"/>。
/// 把两件事分开，是为了避免为"单卡 / 多卡"写特判分支（一个控件实例只能挂在一处，
/// 想复制内容就必须发明 ContentTemplate，那是设计缺陷的信号）。
/// </para>
///
/// <para>
/// 内容有两种写法：
/// </para>
/// <list type="bullet">
///   <item>
///     <b>单段</b>（一种字号）：直接用属性，如
///     <c>&lt;controls:SkeletonCard Variant="Row" Row="BodySm" LineWidths="200,*"/&gt;</c>。
///   </item>
///   <item>
///     <b>多段</b>（字号不同）：把多个 <see cref="SkeletonLines"/> 写进 <c>Content</c>，
///     如下。段与段的区别就是字号，必须分开写。
///   </item>
/// </list>
/// <code>
/// &lt;controls:SkeletonCard Variant="Card"&gt;
///     &lt;StackPanel Spacing="10"&gt;
///         &lt;controls:SkeletonLines Row="Caption" LineWidths="52,40" Spacing="2"/&gt;
///         &lt;controls:SkeletonLines Row="BodySm" LineWidths="120"/&gt;
///         &lt;controls:SkeletonLines Row="Title" LineWidths="110"/&gt;
///     &lt;/StackPanel&gt;
/// &lt;/controls:SkeletonCard&gt;
/// </code>
/// </summary>
public class SkeletonCard : ContentControl
{
    private static readonly string[] VariantClasses = ["variant-card", "variant-tile", "variant-row", "variant-bare"];

    /// <summary>表面形态。</summary>
    public static readonly StyledProperty<SkeletonCardVariant> VariantProperty =
        AvaloniaProperty.Register<SkeletonCard, SkeletonCardVariant>(nameof(Variant));

    /// <summary>单段内容时使用：灰条行高档位。</summary>
    public static readonly StyledProperty<SkeletonRow> RowProperty =
        AvaloniaProperty.Register<SkeletonCard, SkeletonRow>(nameof(Row));

    /// <summary>单段内容时使用：行数。给了 <see cref="LineWidths"/> 时以宽度序列长度为准。</summary>
    public static readonly StyledProperty<int> LineCountProperty =
        AvaloniaProperty.Register<SkeletonCard, int>(nameof(LineCount), 1);

    /// <summary>单段内容时使用：每行宽度，逗号分隔，<c>*</c> 撑满。</summary>
    public static readonly StyledProperty<string?> LineWidthsProperty =
        AvaloniaProperty.Register<SkeletonCard, string?>(nameof(LineWidths));

    /// <summary>单段内容时使用：行间距。</summary>
    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<SkeletonCard, double>(nameof(LineSpacing), 6);

    /// <summary>单段内容时使用：灰条排列方向（横向 = 一排并列小块）。</summary>
    public static readonly StyledProperty<Orientation> LinesOrientationProperty =
        AvaloniaProperty.Register<SkeletonCard, Orientation>(nameof(LinesOrientation), Orientation.Vertical);

    public SkeletonCardVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public SkeletonRow Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public int LineCount
    {
        get => GetValue(LineCountProperty);
        set => SetValue(LineCountProperty, value);
    }

    public string? LineWidths
    {
        get => GetValue(LineWidthsProperty);
        set => SetValue(LineWidthsProperty, value);
    }

    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    public Orientation LinesOrientation
    {
        get => GetValue(LinesOrientationProperty);
        set => SetValue(LinesOrientationProperty, value);
    }

    static SkeletonCard()
    {
        VariantProperty.Changed.AddClassHandler<SkeletonCard>((card, _) => card.ApplyVariant());
        RowProperty.Changed.AddClassHandler<SkeletonCard>((card, _) => card.RebuildLines());
        LineCountProperty.Changed.AddClassHandler<SkeletonCard>((card, _) => card.RebuildLines());
        LineWidthsProperty.Changed.AddClassHandler<SkeletonCard>((card, _) => card.RebuildLines());
        LineSpacingProperty.Changed.AddClassHandler<SkeletonCard>((card, _) => card.RebuildLines());
        LinesOrientationProperty.Changed.AddClassHandler<SkeletonCard>((card, _) => card.RebuildLines());
    }

    public SkeletonCard()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ApplyVariant();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 内容由 XAML 显式给出（多段骨架）时不接管
        if (Content is null)
        {
            RebuildLines();
        }
    }

    private void ApplyVariant()
    {
        // 用样式类而不是直接写 Padding/CornerRadius：内边距与圆角必须与真实卡片共用
        // 同一份令牌，改 Spacing.axaml 时骨架会跟着变，不需要逐页改。
        foreach (var name in VariantClasses)
        {
            Classes.Remove(name);
        }

        var index = Variant switch
        {
            SkeletonCardVariant.Tile => 1,
            SkeletonCardVariant.Row => 2,
            SkeletonCardVariant.Bare => 3,
            _ => 0,
        };

        Classes.Add(VariantClasses[index]);
    }

    /// <summary>单段内容的默认实现：直接放一个 <see cref="SkeletonLines"/>。</summary>
    private void RebuildLines()
    {
        if (Content is not null)
        {
            return;
        }

        Content = new SkeletonLines
        {
            Row = Row,
            LineCount = LineCount,
            LineWidths = LineWidths,
            Spacing = LineSpacing,
            Orientation = LinesOrientation,
        };
    }
}
