using System.Windows.Input;

namespace MarketAssistant.Views;

public partial class ProgressDisplayView : ContentView
{
    // 是否显示进度
    public static readonly BindableProperty IsProgressVisibleProperty =
        BindableProperty.Create(nameof(IsProgressVisible), typeof(bool), typeof(ProgressDisplayView), false, propertyChanged: OnIsProgressVisibleChanged);

    // 分析是否进行中
    public static readonly BindableProperty IsAnalysisInProgressProperty =
        BindableProperty.Create(nameof(IsAnalysisInProgress), typeof(bool), typeof(ProgressDisplayView), false, propertyChanged: OnIsAnalysisInProgressChanged);

    // 分析阶段
    public static readonly BindableProperty AnalysisStageProperty =
        BindableProperty.Create(nameof(AnalysisStage), typeof(string), typeof(ProgressDisplayView), string.Empty, propertyChanged: OnAnalysisStageChanged);


    public bool IsProgressVisible
    {
        get => (bool)GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }

    public bool IsAnalysisInProgress
    {
        get => (bool)GetValue(IsAnalysisInProgressProperty);
        set => SetValue(IsAnalysisInProgressProperty, value);
    }

    public string AnalysisStage
    {
        get => (string)GetValue(AnalysisStageProperty);
        set => SetValue(AnalysisStageProperty, value);
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

    private static void OnIsAnalysisInProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ProgressDisplayView)bindable;
        bool isInProgress = (bool)newValue;

        // 更新圆形进度指示器的运行状态
        control.LoadingIndicator.IsRunning = isInProgress;
    }

    private static void OnAnalysisStageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ProgressDisplayView)bindable;
        control.StageLabel.Text = (string)newValue;
    }

}