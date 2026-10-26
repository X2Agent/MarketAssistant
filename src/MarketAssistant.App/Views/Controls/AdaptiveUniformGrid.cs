using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 自适应列数的 UniformGrid：按可用宽度在 1~<see cref="MaxColumns"/> 列之间切换，
/// 避免写死列数在宽窗口浪费横向空间、在窄窗口挤压卡片内容。
/// 垂直滚动容器内可用宽度即视口宽度，窗口缩放时列数随布局自动更新。
/// </summary>
public class AdaptiveUniformGrid : UniformGrid
{
    /// <summary>单列最小宽度（像素）：可用宽度每达到一个该值就增加一列。</summary>
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<AdaptiveUniformGrid, double>(nameof(MinColumnWidth), 260);

    /// <summary>单列最小宽度（像素）。</summary>
    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>列数上限。</summary>
    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<AdaptiveUniformGrid, int>(nameof(MaxColumns), 3);

    /// <summary>列数上限。</summary>
    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // 垂直滚动场景高度为 Infinity，宽度仍受视口约束；宽度不可用时回退当前布局宽度
        var width = double.IsInfinity(availableSize.Width) ? Bounds.Width : availableSize.Width;
        if (width > 0 && MinColumnWidth > 0)
        {
            Columns = Math.Clamp((int)(width / MinColumnWidth), 1, Math.Max(1, MaxColumns));
        }

        return base.MeasureOverride(availableSize);
    }
}
