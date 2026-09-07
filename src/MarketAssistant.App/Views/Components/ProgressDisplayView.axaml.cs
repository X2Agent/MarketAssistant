using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 进度显示视图（金融终端风格：流程步骤条 + 大号百分比 + 逐分析师状态行）
/// </summary>
public partial class ProgressDisplayView : UserControl
{
    public static readonly StyledProperty<string> AnalysisStageProperty =
        AvaloniaProperty.Register<ProgressDisplayView, string>(nameof(AnalysisStage), string.Empty);

    public static readonly StyledProperty<int> ProgressPercentProperty =
        AvaloniaProperty.Register<ProgressDisplayView, int>(nameof(ProgressPercent), 0);

    public static readonly StyledProperty<AnalysisPhase> AnalysisPhaseProperty =
        AvaloniaProperty.Register<ProgressDisplayView, AnalysisPhase>(
            nameof(AnalysisPhase), AnalysisPhase.Preparing);

    public static readonly StyledProperty<string> FailedAnalystsInfoProperty =
        AvaloniaProperty.Register<ProgressDisplayView, string>(nameof(FailedAnalystsInfo), string.Empty);

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ProgressDisplayView, System.Windows.Input.ICommand?>(nameof(CancelCommand));

    /// <summary>逐分析师状态行集合（由页面 ViewModel 维护并透传）</summary>
    public static readonly StyledProperty<System.Collections.Generic.IEnumerable<AnalystRunItemViewModel>?> AnalystRunItemsProperty =
        AvaloniaProperty.Register<ProgressDisplayView, System.Collections.Generic.IEnumerable<AnalystRunItemViewModel>?>(
            nameof(AnalystRunItems));

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

    /// <summary>
    /// 当前流程阶段，驱动步骤条高亮（准备→分析→聚合→报告→完成）
    /// </summary>
    public AnalysisPhase AnalysisPhase
    {
        get => GetValue(AnalysisPhaseProperty);
        set => SetValue(AnalysisPhaseProperty, value);
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

    public System.Collections.Generic.IEnumerable<AnalystRunItemViewModel>? AnalystRunItems
    {
        get => GetValue(AnalystRunItemsProperty);
        set => SetValue(AnalystRunItemsProperty, value);
    }

    private readonly Border[] _phaseSteps;
    private readonly Border[] _phaseLinks;

    public ProgressDisplayView()
    {
        InitializeComponent();

        _phaseSteps = [PhaseStep0, PhaseStep1, PhaseStep2, PhaseStep3, PhaseStep4];
        _phaseLinks = [PhaseLink0, PhaseLink1, PhaseLink2, PhaseLink3];

        // StyledProperty 变更不走 PropertyChanged 回调注册，这里监听属性值变化以刷新步骤条
        PropertyChanged += OnSelfPropertyChanged;
    }

    private void OnSelfPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == AnalysisPhaseProperty)
        {
            UpdatePhaseBar(e.GetNewValue<AnalysisPhase>());
        }
    }

    /// <summary>
    /// 刷新步骤条状态：当前阶段高亮（active），之前的阶段标记为完成（done）
    /// </summary>
    private void UpdatePhaseBar(AnalysisPhase phase)
    {
        // Completed 视为全部完成；否则当前阶段索引 0..3（Completed 之外）
        var currentIndex = phase switch
        {
            AnalysisPhase.Preparing => 0,
            AnalysisPhase.Analyzing => 1,
            AnalysisPhase.Aggregating => 2,
            AnalysisPhase.Reporting => 3,
            AnalysisPhase.Completed => _phaseSteps.Length,
            _ => 0
        };

        for (var i = 0; i < _phaseSteps.Length; i++)
        {
            var step = _phaseSteps[i];
            step.Classes.Set("active", i == currentIndex);
            step.Classes.Set("done", i < currentIndex);
        }

        for (var i = 0; i < _phaseLinks.Length; i++)
        {
            _phaseLinks[i].Classes.Set("done", i < currentIndex);
        }
    }
}

