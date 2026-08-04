using Avalonia;
using Avalonia.Controls;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 进度显示视图
/// </summary>
public partial class ProgressDisplayView : UserControl
{
    public static readonly StyledProperty<string> AnalysisStageProperty =
        AvaloniaProperty.Register<ProgressDisplayView, string>(nameof(AnalysisStage), string.Empty);

    public static readonly StyledProperty<int> ProgressPercentProperty =
        AvaloniaProperty.Register<ProgressDisplayView, int>(nameof(ProgressPercent), 0);

    public static readonly StyledProperty<string> FailedAnalystsInfoProperty =
        AvaloniaProperty.Register<ProgressDisplayView, string>(nameof(FailedAnalystsInfo), string.Empty);

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ProgressDisplayView, System.Windows.Input.ICommand?>(nameof(CancelCommand));

    public string AnalysisStage
    {
        get => GetValue(AnalysisStageProperty);
        set => SetValue(AnalysisStageProperty, value);
    }

    public int ProgressPercent
    {
        get => GetValue(ProgressPercentProperty);
        set => SetValue(ProgressPercentProperty, value);
    }

    public string FailedAnalystsInfo
    {
        get => GetValue(FailedAnalystsInfoProperty);
        set => SetValue(FailedAnalystsInfoProperty, value);
    }

    public System.Windows.Input.ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ProgressDisplayView()
    {
        InitializeComponent();
    }
}

