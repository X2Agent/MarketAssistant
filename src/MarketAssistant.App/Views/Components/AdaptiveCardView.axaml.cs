using AdaptiveCards;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Avalonia.Markup.Xaml.MarkupExtensions;

namespace MarketAssistant.Views.Components;

public partial class AdaptiveCardView : UserControl
{
    private const int SmallFontSize = 12;
    private const int DefaultFontSize = 14;
    private const int MediumFontSize = 16;
    private const int LargeFontSize = 20;
    private const int ExtraLargeFontSize = 24;
    private const int ContainerSpacing = 8;
    private const int ContainerPadding = 12;
    private const int ContainerCornerRadius = 6;

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
        };

        // Size
        tb.FontSize = textBlock.Size switch
        {
            AdaptiveTextSize.Small => SmallFontSize,
            AdaptiveTextSize.Default => DefaultFontSize,
            AdaptiveTextSize.Medium => MediumFontSize,
            AdaptiveTextSize.Large => LargeFontSize,
            AdaptiveTextSize.ExtraLarge => ExtraLargeFontSize,
            _ => DefaultFontSize
        };

        // Weight
        tb.FontWeight = textBlock.Weight switch
        {
            AdaptiveTextWeight.Lighter => FontWeight.Light,
            AdaptiveTextWeight.Default => FontWeight.Normal,
            AdaptiveTextWeight.Bolder => FontWeight.Bold,
            _ => FontWeight.Normal
        };

        // Color — 对齐到项目真实主题资源（不再依赖幽灵 SystemControl* 画刷）
        tb[!TextBlock.ForegroundProperty] = textBlock.Color switch
        {
            AdaptiveTextColor.Accent => new DynamicResourceExtension("AccentBrush"),
            AdaptiveTextColor.Good => new DynamicResourceExtension("SuccessDeepTextBrush"),
            AdaptiveTextColor.Warning => new DynamicResourceExtension("WarningDarkTextBrush"),
            AdaptiveTextColor.Attention => new DynamicResourceExtension("ErrorDarkTextBrush"),
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

        // Style based on container style —— 两种容器风格一致，仅靠颜色区分语义
        if (container.Style == AdaptiveContainerStyle.Emphasis)
        {
            border[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceVariantBrush");
        }
        else if (container.Style == AdaptiveContainerStyle.Attention)
        {
            border[!Border.BackgroundProperty] = new DynamicResourceExtension("DangerBackgroundBrush");
            border[!Border.BorderBrushProperty] = new DynamicResourceExtension("ErrorDarkTextBrush");
            border.BorderThickness = new Thickness(3, 0, 0, 0);
            border.CornerRadius = new CornerRadius(ContainerCornerRadius, 0, 0, ContainerCornerRadius);
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
            var width = col.Width?.ToLower();

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
            else if (width.EndsWith("px") && double.TryParse(width.TrimEnd('p', 'x'), out double px))
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
            var panel = new StackPanel { Spacing = 4 };

            foreach (var item in col.Items)
            {
                var control = RenderElement(item);
                if (control != null)
                {
                    panel.Children.Add(control);
                }
            }

            var border = new Border { Child = panel };
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
                FontWeight = FontWeight.Medium,
                Margin = new Thickness(0, 0, 0, 6)
            };
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
        var textBlock = new TextBlock
        {
            Text = "Image: " + image.Url,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 10
        };
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
