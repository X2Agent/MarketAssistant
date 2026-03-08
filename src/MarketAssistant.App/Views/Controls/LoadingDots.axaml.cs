using Avalonia;
using Avalonia.Controls;
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
        
        // 默认使用主题色
        if (DotColor == null)
        {
            DotColor = Application.Current?.FindResource("PrimaryBrush") as IBrush;
        }
    }
}

