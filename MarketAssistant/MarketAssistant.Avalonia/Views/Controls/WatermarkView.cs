using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MarketAssistant.Avalonia.Views.Controls;

/// <summary>
/// 水印视图组件 - 基础自绘控件
/// 根据 Avalonia 最佳实践，继承自 Control 并重写 Render 方法
/// </summary>
public class WatermarkView : Control
{
    // 水印文本属性
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<WatermarkView, string>(nameof(Text), "水印");

    // 水印透明度属性
    public static readonly StyledProperty<double> OpacityValueProperty =
        AvaloniaProperty.Register<WatermarkView, double>(nameof(OpacityValue), 0.2);

    // 水印角度属性
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<WatermarkView, double>(nameof(Angle), -45.0);

    // 水印颜色属性
    public static readonly StyledProperty<IBrush> TextBrushProperty =
        AvaloniaProperty.Register<WatermarkView, IBrush>(nameof(TextBrush), Brushes.Gray);

    // 水印字体大小属性
    public static readonly StyledProperty<double> WatermarkFontSizeProperty =
        AvaloniaProperty.Register<WatermarkView, double>(nameof(FontSize), 24.0);

    // 水印字体族属性
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<WatermarkView, FontFamily>(nameof(FontFamily), FontFamily.Default);

    // 水印重复次数（水平）属性
    public static readonly StyledProperty<int> HorizontalRepeatCountProperty =
        AvaloniaProperty.Register<WatermarkView, int>(nameof(HorizontalRepeatCount), 3);

    // 水印重复次数（垂直）属性
    public static readonly StyledProperty<int> VerticalRepeatCountProperty =
        AvaloniaProperty.Register<WatermarkView, int>(nameof(VerticalRepeatCount), 5);

    /// <summary>
    /// 水印文本
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// 水印透明度
    /// </summary>
    public double OpacityValue
    {
        get => GetValue(OpacityValueProperty);
        set => SetValue(OpacityValueProperty, value);
    }

    /// <summary>
    /// 水印角度
    /// </summary>
    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    /// <summary>
    /// 水印颜色画刷
    /// </summary>
    public IBrush TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>
    /// 水印字体大小
    /// </summary>
    public double FontSize
    {
        get => GetValue(WatermarkFontSizeProperty);
        set => SetValue(WatermarkFontSizeProperty, value);
    }

    /// <summary>
    /// 水印字体族
    /// </summary>
    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// 水平重复次数
    /// </summary>
    public int HorizontalRepeatCount
    {
        get => GetValue(HorizontalRepeatCountProperty);
        set => SetValue(HorizontalRepeatCountProperty, value);
    }

    /// <summary>
    /// 垂直重复次数
    /// </summary>
    public int VerticalRepeatCount
    {
        get => GetValue(VerticalRepeatCountProperty);
        set => SetValue(VerticalRepeatCountProperty, value);
    }

    static WatermarkView()
    {
        // 设置属性变更时重绘
        AffectsRender<WatermarkView>(
            TextProperty,
            OpacityValueProperty,
            AngleProperty,
            TextBrushProperty,
            WatermarkFontSizeProperty,
            FontFamilyProperty,
            HorizontalRepeatCountProperty,
            VerticalRepeatCountProperty);
    }

    public WatermarkView()
    {
        // 设置控件属性
        IsHitTestVisible = false; // 允许点击穿透
    }

    /// <summary>
    /// 重写 Render 方法进行自定义绘制
    /// </summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (string.IsNullOrEmpty(Text) || HorizontalRepeatCount <= 0 || VerticalRepeatCount <= 0)
            return;

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        try
        {
            // 创建文本格式
            var typeface = new Typeface(FontFamily);
            var formattedText = new FormattedText(
                Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                TextBrush);

            // 计算每个水印的大小和间距
            var cellWidth = bounds.Width / HorizontalRepeatCount;
            var cellHeight = bounds.Height / VerticalRepeatCount;

            // 绘制水印网格
            for (int row = 0; row < VerticalRepeatCount; row++)
            {
                for (int col = 0; col < HorizontalRepeatCount; col++)
                {
                    // 计算当前水印的中心位置
                    var centerX = col * cellWidth + cellWidth / 2;
                    var centerY = row * cellHeight + cellHeight / 2;

                    // 保存当前绘制状态
                    using (context.PushTransform(Matrix.CreateTranslation(centerX, centerY)))
                    using (context.PushTransform(Matrix.CreateRotation(Math.PI * Angle / 180.0)))
                    using (context.PushOpacity(OpacityValue))
                    {
                        // 计算文本绘制位置（相对于旋转中心）
                        var textX = -formattedText.Width / 2;
                        var textY = -formattedText.Height / 2;

                        // 绘制文本
                        context.DrawText(formattedText, new Point(textX, textY));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"绘制水印失败: {ex.Message}");
        }
    }
}
