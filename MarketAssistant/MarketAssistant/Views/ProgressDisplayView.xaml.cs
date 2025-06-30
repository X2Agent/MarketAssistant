using System.Windows.Input;

namespace MarketAssistant.Views;

public partial class ProgressDisplayView : ContentView
{
    // 是否显示进度
    public static readonly BindableProperty IsProgressVisibleProperty =
        BindableProperty.Create(nameof(IsProgressVisible), typeof(bool), typeof(ProgressDisplayView), false, propertyChanged: OnIsProgressVisibleChanged);

    // 分析进度
    public static readonly BindableProperty AnalysisProgressProperty =
        BindableProperty.Create(nameof(AnalysisProgress), typeof(int), typeof(ProgressDisplayView), 0, propertyChanged: OnAnalysisProgressChanged);

    // 分析阶段
    public static readonly BindableProperty AnalysisStageProperty =
        BindableProperty.Create(nameof(AnalysisStage), typeof(string), typeof(ProgressDisplayView), string.Empty, propertyChanged: OnAnalysisStageChanged);

    // 当前分析师
    public static readonly BindableProperty CurrentAnalystProperty =
        BindableProperty.Create(nameof(CurrentAnalyst), typeof(string), typeof(ProgressDisplayView), string.Empty, propertyChanged: OnCurrentAnalystChanged);

    public bool IsProgressVisible
    {
        get => (bool)GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }

    public int AnalysisProgress
    {
        get => (int)GetValue(AnalysisProgressProperty);
        set => SetValue(AnalysisProgressProperty, value);
    }

    public string AnalysisStage
    {
        get => (string)GetValue(AnalysisStageProperty);
        set => SetValue(AnalysisStageProperty, value);
    }

    public string CurrentAnalyst
    {
        get => (string)GetValue(CurrentAnalystProperty);
        set => SetValue(CurrentAnalystProperty, value);
    }

    public ProgressDisplayView()
    {
        InitializeComponent();
        this.Opacity = 0;
    }

    private static void OnIsProgressVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ProgressDisplayView)bindable;
        bool isVisible = (bool)newValue;

        // 使用动画显示或隐藏进度条
        if (isVisible)
        {
            control.FadeTo(1, 250, Easing.CubicOut);
        }
        else
        {
            control.FadeTo(0, 250, Easing.CubicIn);
        }

        control.ProgressBorder.IsVisible = isVisible;
    }

    private static void OnAnalysisProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ProgressDisplayView)bindable;
        int progress = (int)newValue;

        // 更新进度条和进度文本
        control.AnalysisProgressBar.Progress = progress / 100.0;
        control.ProgressLabel.Text = $"{progress}%";
    }

    private static void OnAnalysisStageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ProgressDisplayView)bindable;
        control.StageLabel.Text = (string)newValue;
    }

    private static void OnCurrentAnalystChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ProgressDisplayView)bindable;
        control.AnalystLabel.Text = (string)newValue;
    }

    private static void OnShowDetailsCommandChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ProgressDisplayView)bindable;
        control.ShowDetailsButton.Command = (ICommand)newValue;
    }
}