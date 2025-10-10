using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 进度显示视图
/// </summary>
public partial class ProgressDisplayView : UserControl
{
    public static readonly StyledProperty<bool> IsProgressVisibleProperty =
        AvaloniaProperty.Register<ProgressDisplayView, bool>(nameof(IsProgressVisible), false);

    public static readonly StyledProperty<bool> IsAnalysisInProgressProperty =
        AvaloniaProperty.Register<ProgressDisplayView, bool>(nameof(IsAnalysisInProgress), false);

    public static readonly StyledProperty<string> AnalysisStageProperty =
        AvaloniaProperty.Register<ProgressDisplayView, string>(nameof(AnalysisStage), string.Empty);

    public bool IsProgressVisible
    {
        get => GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }

    public bool IsAnalysisInProgress
    {
        get => GetValue(IsAnalysisInProgressProperty);
        set => SetValue(IsAnalysisInProgressProperty, value);
    }

    public string AnalysisStage
    {
        get => GetValue(AnalysisStageProperty);
        set => SetValue(AnalysisStageProperty, value);
    }

    public ProgressDisplayView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsProgressVisibleProperty)
        {
            this.IsVisible = (bool)(change.NewValue ?? false);
        }
        else if (change.Property == AnalysisStageProperty)
        {
            var stageLabel = this.FindControl<TextBlock>("StageLabel");
            if (stageLabel != null)
            {
                stageLabel.Text = change.NewValue?.ToString() ?? string.Empty;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

