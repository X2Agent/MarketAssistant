using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 三点波浪加载动画组件
/// </summary>
public partial class LoadingDots : UserControl
{
    /// <summary>
    /// 圆点大小属性
    /// </summary>
    public static readonly StyledProperty<double> DotSizeProperty =
        AvaloniaProperty.Register<LoadingDots, double>(nameof(DotSize), 12.0);

    /// <summary>
    /// 圆点颜色属性
    /// </summary>
    public static readonly StyledProperty<IBrush?> DotColorProperty =
        AvaloniaProperty.Register<LoadingDots, IBrush?>(nameof(DotColor));

    /// <summary>
    /// 圆点间距属性
    /// </summary>
    public static readonly StyledProperty<double> DotSpacingProperty =
        AvaloniaProperty.Register<LoadingDots, double>(nameof(DotSpacing), 8.0);

    /// <summary>
    /// 圆点大小
    /// </summary>
    public double DotSize
    {
        get => GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    /// <summary>
    /// 圆点颜色
    /// </summary>
    public IBrush? DotColor
    {
        get => GetValue(DotColorProperty);
        set => SetValue(DotColorProperty, value);
    }

    /// <summary>
    /// 圆点间距
    /// </summary>
    public double DotSpacing
    {
        get => GetValue(DotSpacingProperty);
        set => SetValue(DotSpacingProperty, value);
    }

    public LoadingDots()
    {
        InitializeComponent();

        // 默认取强调色：DynamicResource 让它在主题切换后自动重算。
        // 不能用 Application.FindResource("PrimaryBrush") —— v3 已删除 v2 的 Primary* 键，
        // 取不到时返回 UnsetValue，圆点会因 Fill 为 null 而整个不可见。
        // 使用点在 XAML 里显式给了 DotColor 时不覆盖它。
        if (DotColor is null)
        {
            this[!DotColorProperty] = new DynamicResourceExtension("AccentBrush");
        }
    }
}

