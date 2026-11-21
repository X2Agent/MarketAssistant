using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 骨架列表：把同一个骨架重复 N 次，用于列表页首屏。
///
/// <para>
/// 刻意独立于 <see cref="SkeletonCard"/>：一个 <c>SkeletonCard</c> 实例只能挂在一个地方，
/// 若把"重复"做成它的属性，就必须发明 ContentTemplate 才能复制内容，还要为
/// "单卡 / 多卡"写特判分支。用 ItemsControl 的 <see cref="ItemsControl.ItemTemplate"/>
/// 天然为每一项创建独立实例，没有特例。
/// </para>
///
/// <code>
/// &lt;controls:SkeletonList Count="5" Spacing="8"&gt;
///     &lt;controls:SkeletonList.ItemTemplate&gt;
///         &lt;DataTemplate&gt;
///             &lt;controls:SkeletonCard Variant="Row"&gt;…&lt;/controls:SkeletonCard&gt;
///         &lt;/DataTemplate&gt;
///     &lt;/controls:SkeletonList.ItemTemplate&gt;
/// &lt;/controls:SkeletonList&gt;
/// </code>
///
/// 需要多列网格（两列瓦片、四张指标卡）时显式给 <c>ItemsPanel</c>（如 <c>UniformGrid</c>），
/// 此时 <c>Spacing</c> 由该面板或条目的 Margin 自行负责。
/// </summary>
public class SkeletonList : ItemsControl
{
    /// <summary>重复次数。</summary>
    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<SkeletonList, int>(nameof(Count));

    /// <summary>条目间距（像素），仅在使用默认面板时生效。</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SkeletonList, double>(nameof(Spacing));

    /// <summary>默认面板的排列方向。</summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<SkeletonList, Orientation>(nameof(Orientation), Orientation.Vertical);

    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    static SkeletonList()
    {
        CountProperty.Changed.AddClassHandler<SkeletonList>((list, _) => list.Rebuild());
        OrientationProperty.Changed.AddClassHandler<SkeletonList>((list, _) => list.ApplyDefaultPanel());
        SpacingProperty.Changed.AddClassHandler<SkeletonList>((list, _) => list.ApplyDefaultPanel());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // XAML 显式给了 ItemsPanel（如 UniformGrid）就完全交给它
        if (ItemsPanel is null)
        {
            ApplyDefaultPanel();
        }

        if (ItemsSource is null)
        {
            Rebuild();
        }
    }

    private void ApplyDefaultPanel()
    {
        ItemsPanel = new FuncTemplate<Panel?>(
            () => new StackPanel { Orientation = Orientation, Spacing = Spacing });
    }

    private void Rebuild()
    {
        // 条目本身无数据含义，只作为模板的占位载体；顺序编号让 DataTemplate 可复用
        ItemsSource = Enumerable.Range(0, Math.Max(0, Count)).ToArray();
    }
}
