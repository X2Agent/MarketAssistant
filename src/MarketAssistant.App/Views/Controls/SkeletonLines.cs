using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MarketAssistant.Views.Controls;

/// <summary>骨架灰条的行高档位，取值与 Spacing.axaml 的行高令牌一一对应。</summary>
public enum SkeletonRow
{
    /// <summary>12px 辅助文字（高 18）。</summary>
    Caption,

    /// <summary>14px 正文（高 21），最常用的档。</summary>
    BodySm,

    /// <summary>16px 阅读型正文（高 24）。</summary>
    Body,

    /// <summary>24px 标题（高 32）。</summary>
    Title,
}

/// <summary>
/// 一组骨架灰条：<b>同一行高</b>、N 行、每行给一个宽度。
///
/// 高度由 <c>skeleton-line-*</c> 样式按行高令牌给出，保证与真实文字逐像素同高
/// （这是"骨架不跳动"的唯一来源）；本控件只负责"几行、每行多宽、行间留多少"。
///
/// <para>
/// 一个段落一种字号。真实卡片里"代码 12 / 名称 14 / 价格 24"这种混排是多段，
/// 用多个本控件纵向排列表达——不要试图用一个控件的字符串编码二维结构，
/// 那会丢掉每段各自的字号，画出一排等高的错误灰条。
/// </para>
/// <code>
/// &lt;!-- 三段：标题 14、两行说明 12 --&gt;
/// &lt;StackPanel Spacing="4"&gt;
///     &lt;controls:SkeletonLines Row="BodySm" LineWidths="96"/&gt;
///     &lt;controls:SkeletonLines Row="Caption" LineWidths="*,260"/&gt;
/// &lt;/StackPanel&gt;
/// </code>
/// </summary>
public class SkeletonLines : StackPanel
{
    /// <summary>行高档位。</summary>
    public static readonly StyledProperty<SkeletonRow> RowProperty =
        AvaloniaProperty.Register<SkeletonLines, SkeletonRow>(nameof(Row));

    /// <summary>行数。给了 <see cref="LineWidths"/> 时以宽度序列长度为准。</summary>
    public static readonly StyledProperty<int> LineCountProperty =
        AvaloniaProperty.Register<SkeletonLines, int>(nameof(LineCount), 1);

    /// <summary>
    /// 每行宽度，逗号分隔；<c>*</c> 表示该行撑满。<b>行数以条目数为准</b>：
    /// 只想要 N 行撑满的灰条时不要写此项，只写 <see cref="LineCount"/>
    /// （<c>LineCount="N" LineWidths="*"</c> 只会得到 1 行）。
    /// </summary>
    public static readonly StyledProperty<string?> LineWidthsProperty =
        AvaloniaProperty.Register<SkeletonLines, string?>(nameof(LineWidths));

    /// <summary>
    /// 覆盖行高（像素）。仅用于非文字形态的占位（盘口档位、周期按钮、进度条等），
    /// 留空则跟随 <see cref="Row"/> 对应的行高令牌。
    /// </summary>
    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<SkeletonLines, double>(nameof(RowHeight), double.NaN);

    /// <summary>横向排列时每根灰条的宽度（像素）。仅 <c>Orientation=Horizontal</c> 生效。</summary>
    public static readonly StyledProperty<double> LineWidthProperty =
        AvaloniaProperty.Register<SkeletonLines, double>(nameof(LineWidth), 68);

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

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double LineWidth
    {
        get => GetValue(LineWidthProperty);
        set => SetValue(LineWidthProperty, value);
    }

    static SkeletonLines()
    {
        RowProperty.Changed.AddClassHandler<SkeletonLines>((lines, _) => lines.Rebuild());
        LineCountProperty.Changed.AddClassHandler<SkeletonLines>((lines, _) => lines.Rebuild());
        LineWidthsProperty.Changed.AddClassHandler<SkeletonLines>((lines, _) => lines.Rebuild());
        RowHeightProperty.Changed.AddClassHandler<SkeletonLines>((lines, _) => lines.Rebuild());
        LineWidthProperty.Changed.AddClassHandler<SkeletonLines>((lines, _) => lines.Rebuild());
        SpacingProperty.Changed.AddClassHandler<SkeletonLines>((lines, _) => lines.Rebuild());
        OrientationProperty.Changed.AddClassHandler<SkeletonLines>((lines, _) => lines.Rebuild());

        OrientationProperty.OverrideDefaultValue<SkeletonLines>(Orientation.Vertical);

        // 行距默认 6：真实卡片里文字行间距普遍 4–8，0 会让多行灰条粘成一整块
        SpacingProperty.OverrideDefaultValue<SkeletonLines>(6);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 样式类只在挂到可视树后才能解析；属性变化由上面的 Changed 处理
        if (Children.Count == 0)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Children.Clear();

        // 横向：一排等宽小块（周期按钮、标签），宽度统一由 LineWidth 决定
        if (Orientation == Orientation.Horizontal)
        {
            for (var i = 0; i < Math.Max(0, LineCount); i++)
            {
                Children.Add(CreateBar(LineWidth, HorizontalAlignment.Stretch));
            }

            return;
        }

        var widths = ParseWidths();
        var count = widths.Count > 0 ? widths.Count : Math.Max(0, LineCount);

        for (var i = 0; i < count; i++)
        {
            var width = widths.Count == 0 ? (double?)null : widths[i];
            Children.Add(CreateBar(width, AlignmentForBar()));
        }
    }

    private HorizontalAlignment AlignmentForBar()
    {
        // 横向排列时每格铺满；纵向时默认靠左，但若控件自身显式指定了 Right/Center
        // （如右栏指标的数值列），就跟随它——否则数字列会错位。
        if (Orientation == Orientation.Horizontal)
        {
            return HorizontalAlignment.Stretch;
        }

        return HorizontalAlignment == HorizontalAlignment.Stretch
            ? HorizontalAlignment.Left
            : HorizontalAlignment;
    }

    private Border CreateBar(double? width, HorizontalAlignment alignment)
    {
        var bar = new Border();
        bar.Classes.Add("skeleton");
        bar.Classes.Add(Row switch
        {
            SkeletonRow.Caption => "skeleton-line-caption",
            SkeletonRow.Body => "skeleton-line-body",
            SkeletonRow.Title => "skeleton-line-title",
            _ => "skeleton-line-body-sm",
        });

        if (!double.IsNaN(RowHeight))
        {
            bar.Height = RowHeight;
        }

        if (width is { } value)
        {
            bar.Width = value;
            bar.HorizontalAlignment = alignment;
        }

        return bar;
    }

    private List<double?> ParseWidths()
    {
        var widths = new List<double?>();

        if (string.IsNullOrWhiteSpace(LineWidths))
        {
            return widths;
        }

        foreach (var part in LineWidths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "*")
            {
                widths.Add(null);
            }
            else if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                widths.Add(value);
            }
        }

        return widths;
    }
}
