using AdaptiveCards;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace MarketAssistant.Views.Components;

public partial class AdaptiveCardView : UserControl
{
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
            AdaptiveTextSize.Small => 12,
            AdaptiveTextSize.Default => 14,
            AdaptiveTextSize.Medium => 16,
            AdaptiveTextSize.Large => 20,
            AdaptiveTextSize.ExtraLarge => 24,
            _ => 14
        };

        // Weight
        tb.FontWeight = textBlock.Weight switch
        {
            AdaptiveTextWeight.Lighter => FontWeight.Light,
            AdaptiveTextWeight.Default => FontWeight.Normal,
            AdaptiveTextWeight.Bolder => FontWeight.Bold,
            _ => FontWeight.Normal
        };

        // Color with Theme Support
        if (textBlock.Color == AdaptiveTextColor.Accent)
            tb[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundAccentBrush");
        else if (textBlock.Color == AdaptiveTextColor.Good)
            tb[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundSuccessBrush"); // Or a custom resource
        else if (textBlock.Color == AdaptiveTextColor.Warning)
            tb[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundCautionBrush"); // Or a custom resource
        else if (textBlock.Color == AdaptiveTextColor.Attention)
            tb[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlErrorTextForegroundBrush");
        else if (textBlock.IsSubtle)
            tb[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        else
            tb[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush");

        // Fallback for custom brushes if system ones aren't perfect, but for now system ones are safer for light/dark

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
            Spacing = 8
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
            Child = stackPanel
        };

        // Style based on container style
        if (container.Style == AdaptiveContainerStyle.Emphasis)
        {
            // Use a dynamic resource for background to support dark mode
            border[!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush");
            border.Padding = new Thickness(8);
            border.CornerRadius = new CornerRadius(4);
        }
        else if (container.Style == AdaptiveContainerStyle.Attention)
        {
             // Light red background for attention
             border.Background = new SolidColorBrush(Color.Parse("#20FF0000")); 
             border.Padding = new Thickness(8);
             border.CornerRadius = new CornerRadius(4);
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
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto)); // Title
        grid.ColumnDefinitions.Add(new ColumnDefinition(16, GridUnitType.Pixel)); // Spacing
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star)); // Value

        for (int i = 0; i < factSet.Facts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));

            var fact = factSet.Facts[i];

            var title = new TextBlock
            {
                Text = fact.Title,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            // Use dynamic resource for text color
            title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush");
            
            Grid.SetRow(title, i);
            Grid.SetColumn(title, 0);

            var value = new TextBlock
            {
                Text = fact.Value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            // Use dynamic resource for text color (slightly subtler if desired, or same)
            value[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");

            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);

            grid.Children.Add(title);
            grid.Children.Add(value);
        }

        return grid;
    }

    private Control RenderImage(AdaptiveImage image)
    {
        // Placeholder for image rendering
        var border = new Border
        {
            Background = Brushes.LightGray,
            Height = 100,
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = "Image: " + image.Url,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10
            }
        };

        if (image.PixelHeight > 0) border.Height = image.PixelHeight;

        return border;
    }
}
