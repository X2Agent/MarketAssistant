using Avalonia;
using Avalonia.Controls;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 进度显示视图
/// </summary>
public partial class ProgressDisplayView : UserControl
{
    public static readonly StyledProperty<bool> IsAnalysisInProgressProperty =
        AvaloniaProperty.Register<ProgressDisplayView, bool>(nameof(IsAnalysisInProgress), false);

    public static readonly StyledProperty<string> AnalysisStageProperty =
        AvaloniaProperty.Register<ProgressDisplayView, string>(nameof(AnalysisStage), string.Empty);

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
}

