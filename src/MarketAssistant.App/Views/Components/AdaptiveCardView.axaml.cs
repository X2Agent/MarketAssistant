using System;
using AdaptiveCards;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace MarketAssistant.Views.Components;

public partial class AdaptiveCardView : UserControl
{
    // Adaptive Card 的字号档位映射到应用的五档（12/14/16/24/32）。
    // 卡片标准里的 20 与 24 在应用内没有对应档，就近落到 Title(24) 与 Display(32)，
    // 避免外部卡片把第六、第七档字号带进界面。
    private const int SmallFontSize = 12;
    private const int DefaultFontSize = 14;
    private const int MediumFontSize = 16;
    private const int LargeFontSize = 24;
    private const int ExtraLargeFontSize = 32;
    private const int ContainerSpacing = 8;
    private const int ContainerPadding = 12;
    private const int ContainerCornerRadius = 6;
    private const int ColumnGutter = 16;
    private const int SmallColumnGutter = 8;

    public static readonly StyledProperty<AdaptiveCard?> CardProperty =
        AvaloniaProperty.Register<AdaptiveCardView, AdaptiveCard?>(nameof(Card));

    public AdaptiveCard? Card
    {
        get => GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    public AdaptiveCardView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardProperty)
        {
            RenderCard(change.NewValue as AdaptiveCard);
        }
    }

    private void RenderCard(AdaptiveCard? card)
    {
        CardContainer.Children.Clear();
        if (card == null) return;

        foreach (var element in card.Body)
        {
            var control = RenderElement(element);
            if (control != null)
            {
                CardContainer.Children.Add(control);
            }
        }
    }

    private Control? RenderElement(AdaptiveElement element)
    {
        return element switch
        {
            AdaptiveTextBlock textBlock => RenderTextBlock(textBlock),
            AdaptiveContainer container => RenderContainer(container),
            AdaptiveColumnSet columnSet => RenderColumnSet(columnSet),
            AdaptiveFactSet factSet => RenderFactSet(factSet),
            AdaptiveImage image => RenderImage(image),
            _ => null // Unsupported element
        };
    }

    private Control RenderTextBlock(AdaptiveTextBlock textBlock)
    {
        var tb = new TextBlock
        {
            Text = textBlock.Text,
            TextWrapping = textBlock.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            // Size
            FontSize = textBlock.Size switch
            {
                AdaptiveTextSize.Small => SmallFontSize,
                AdaptiveTextSize.Default => DefaultFontSize,
                AdaptiveTextSize.Medium => MediumFontSize,
                AdaptiveTextSize.Large => LargeFontSize,
                AdaptiveTextSize.ExtraLarge => ExtraLargeFontSize,
                _ => DefaultFontSize
            },
        };

        // Weight 走主题令牌：卡片里的 Bolder 只是"相对更重"，不是 Bold 字面量，
        // 否则暗色模式下会白字发粗（design.md 3.2 要求字重随主题降档）
        tb[!TextBlock.FontWeightProperty] = textBlock.Weight switch
        {
            AdaptiveTextWeight.Bolder => new DynamicResourceExtension("WeightTitle"),
            _ => new DynamicResourceExtension("WeightBody"),
        };

        // Color — 对齐到项目真实主题资源（不再依赖幽灵 SystemControl* 画刷）
        tb[!TextBlock.ForegroundProperty] = textBlock.Color switch
        {
            AdaptiveTextColor.Accent => new DynamicResourceExtension("AccentBrush"),
            AdaptiveTextColor.Good => new DynamicResourceExtension("SuccessPanelTextBrush"),
            AdaptiveTextColor.Warning => new DynamicResourceExtension("WarningPanelTextBrush"),
            AdaptiveTextColor.Attention => new DynamicResourceExtension("DangerPanelTextBrush"),
            _ => textBlock.IsSubtle
                ? new DynamicResourceExtension("TextSecondaryBrush")
                : new DynamicResourceExtension("TextPrimaryBrush")
        };

        // Alignment
        tb.TextAlignment = textBlock.HorizontalAlignment switch
        {
            AdaptiveHorizontalAlignment.Left => TextAlignment.Left,
            AdaptiveHorizontalAlignment.Center => TextAlignment.Center,
            AdaptiveHorizontalAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        return tb;
    }

    private Control RenderContainer(AdaptiveContainer container)
    {
        var stackPanel = new StackPanel
        {
            Spacing = ContainerSpacing
        };

        foreach (var item in container.Items)
        {
            var control = RenderElement(item);
            if (control != null)
            {
                stackPanel.Children.Add(control);
            }
        }

        var border = new Border
        {
            Child = stackPanel,
            Padding = new Thickness(ContainerPadding),
            CornerRadius = new CornerRadius(ContainerCornerRadius)
        };

        // Style based on container style —— 两种容器风格一致，仅靠颜色区分语义。
        // 卡片外壳是次级表面（SurfaceVariantBrush），Emphasis 容器因此取卡片白，
        // 用"灰面上的白块"表达强调；同色叠加会完全看不见。
        if (container.Style == AdaptiveContainerStyle.Emphasis)
        {
            border[!Border.BackgroundProperty] = new DynamicResourceExtension("CardBackgroundBrush");
        }
        else if (container.Style == AdaptiveContainerStyle.Attention)
        {
            border[!Border.BackgroundProperty] = new DynamicResourceExtension("DangerPanelBackgroundBrush");
        }

        return border;
    }

    private Control RenderColumnSet(AdaptiveColumnSet columnSet)
    {
        var grid = new Grid();

        // Define columns
        for (int i = 0; i < columnSet.Columns.Count; i++)
        {
            var col = columnSet.Columns[i];
            var width = col.Width?.ToLowerInvariant();

            if (width == "auto")
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            }
            else if (width == "stretch" || string.IsNullOrEmpty(width))
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            }
            else if (double.TryParse(width, out double w)) // Weighted
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(w, GridUnitType.Star));
            }
            else if (width.EndsWith("px", StringComparison.Ordinal)
                 && double.TryParse(width.TrimEnd('p', 'x'), out double px))
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(px, GridUnitType.Pixel));
            }
            else
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            }
        }

        // Add children
        for (int i = 0; i < columnSet.Columns.Count; i++)
        {
            var col = columnSet.Columns[i];
            var panel = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = col.VerticalContentAlignment switch
                {
                    AdaptiveVerticalContentAlignment.Center => VerticalAlignment.Center,
                    AdaptiveVerticalContentAlignment.Bottom => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Top
                }
            };

            foreach (var item in col.Items)
            {
                var control = RenderElement(item);
                if (control != null)
                {
                    panel.Children.Add(control);
                }
            }

            // 卡片标准里 Spacing 表示"与前一个元素的间距"：列与列之间的间距由后一列的 Spacing 决定，
            // 用左外边距实现，不另占列，这样 50% / 50% 的等宽列仍严格等宽
            var gutter = i == 0 ? 0 : col.Spacing switch
            {
                AdaptiveSpacing.None => 0,
                AdaptiveSpacing.Small => SmallColumnGutter,
                _ => ColumnGutter
            };
            var border = new Border { Child = panel, Margin = new Thickness(gutter, 0, 0, 0) };
            Grid.SetColumn(border, i);
            grid.Children.Add(border);
        }

        return grid;
    }

    private Control RenderFactSet(AdaptiveFactSet factSet)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto, 20, *")
        };

        for (int i = 0; i < factSet.Facts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));

            var fact = factSet.Facts[i];

            var title = new TextBlock
            {
                Text = fact.Title,
                Margin = new Thickness(0, 0, 0, 6)
            };
            title[!TextBlock.FontWeightProperty] = new DynamicResourceExtension("WeightEmphasis");
            title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");

            Grid.SetRow(title, i);
            Grid.SetColumn(title, 0);

            var value = new TextBlock
            {
                Text = fact.Value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            value[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimaryBrush");

            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);

            grid.Children.Add(title);
            grid.Children.Add(value);
        }

        return grid;
    }

    private Control RenderImage(AdaptiveImage image)
    {
        var urlStr = image.Url?.ToString() ?? string.Empty;
        if (urlStr.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            // Svg 控件无无参构造，须显式传入 baseUri；avares:// 绝对 URI 不依赖 baseUri 解析。
            // 该构造函数签名把 baseUri 标注为非空 Uri，故用 (Uri)null!：
            // 先转型以在 Svg(Uri) 与 Svg(IServiceProvider) 之间消歧义，再以 null! 抑制
            // “不可为 null 实参”告警。此处传 null 是控件的既定用法（依赖绝对 URI 解析）。
            return new Avalonia.Svg.Skia.Svg((Uri)null!)
            {
                Path = urlStr,
                Width = image.PixelWidth > 0 ? image.PixelWidth : 20,
                Height = image.PixelHeight > 0 ? image.PixelHeight : 20,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        var textBlock = new TextBlock
        {
            Text = "Image: " + image.Url,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        textBlock[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("CaptionFontSize");
        textBlock[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");

        var border = new Border
        {
            Height = 100,
            CornerRadius = new CornerRadius(4),
            Child = textBlock
        };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceVariantBrush");

        if (image.PixelHeight > 0) border.Height = image.PixelHeight;

        return border;
    }
}
