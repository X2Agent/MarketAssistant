using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Transformation;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 复制成功反馈：小对勾弹性出现，2.5s 后自动淡出。
///
/// 动效性格：
///  - 出现用 BackEase（振幅 0.15 ≈ 12% 过冲），带一点"Q 弹"，符合"复制成功"这种
///    轻量、可逆、用户主动触发的动作；
///  - 消失不做回弹，只做 180ms 淡出——退场再弹会显得拖沓。
///
/// 用法：调用 <see cref="Show"/> 后无需其他操作，控件自行计时并隐藏。
/// 重复调用 Show 会重置计时（连续复制多次只保留最后一次的对勾窗口）。
/// </summary>
public class CopyFeedback : UserControl
{
    private static TimeSpan EnterDuration => MotionTokens.CopyFeedbackFade;
    private static TimeSpan HoldDuration => MotionTokens.CopyFeedbackHold;
    private static TimeSpan FadeDuration => MotionTokens.CopyFeedbackFade;

    /// <summary>提示文字，默认"已复制"。</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<CopyFeedback, string>(nameof(Text), "已复制");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// 触发令牌：值每变化一次就播放一次反馈。
    ///
    /// 之所以不用"调用 Show()"的方式，是因为本控件常驻在聊天气泡里，
    /// 由数据模板实例化，代码后置拿不到实例。改由 ViewModel 递增一个计数器，
    /// 这里监听变化即可，命令与视图之间无需互相引用。
    /// </summary>
    public static readonly StyledProperty<int> TriggerProperty =
        AvaloniaProperty.Register<CopyFeedback, int>(nameof(Trigger));

    public int Trigger
    {
        get => GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    private readonly Border _chip;
    private readonly TextBlock _label;
    private CancellationTokenSource? _cts;

    private static readonly TransformOperations ScaledDown = TransformOperations.Parse("scale(0.85)");
    private static readonly TransformOperations ScaledUp = TransformOperations.Parse("scale(1)");

    public CopyFeedback()
    {
        _label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        _label.Bind(TextBlock.TextProperty, this.GetObservable(TextProperty));

        var checkMark = new TextBlock
        {
            Text = "\u2713",
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 字号、字重、前景色一律走令牌：字面量不会跟随暗色自动降档（见 design.md 3.2）
        _label[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("CaptionFontSize");
        _label[!TextBlock.FontWeightProperty] = new DynamicResourceExtension("WeightBody");
        _label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextOnEmphasisBrush");
        checkMark[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("CaptionFontSize");
        checkMark[!TextBlock.FontWeightProperty] = new DynamicResourceExtension("WeightEmphasis");
        checkMark[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextOnEmphasisBrush");

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { checkMark, _label },
        };
        content[!StackPanel.SpacingProperty] = new DynamicResourceExtension("TinySpacing");

        _chip = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Opacity = 0,
            RenderTransform = ScaledDown,
            Child = content,
        };
        _chip[!Border.CornerRadiusProperty] = new DynamicResourceExtension("TinyCornerRadius");
        _chip[!Border.PaddingProperty] = new DynamicResourceExtension("CompactButtonPadding");
        _chip.Bind(Border.BackgroundProperty, this.GetObservable(BackgroundProperty));

        // 未显式指定背景时给一个中性深底，保证对勾与文字始终可读
        if (Background is null)
        {
            _chip.Bind(Border.BackgroundProperty,
                new DynamicResourceExtension("TextPrimaryBrush"));
        }

        Content = new Panel { Children = { _chip } };
    }

    /// <summary>播放"复制成功"反馈。连续调用会重启计时。</summary>
    public void Show()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;

        _ = RunAsync(cts.Token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            _chip.IsVisible = true;

            // 入场：回弹放大 + 淡入
            _chip.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = EnterDuration,
                    Easing = new CubicEaseOut(),
                },
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = EnterDuration,
                    Easing = new BackEaseOut(),
                },
            };

            token.ThrowIfCancellationRequested();
            await Task.Delay(16, token).ConfigureAwait(true);

            _chip.RenderTransform = ScaledUp;
            _chip.Opacity = 1;

            await Task.Delay(HoldDuration, token).ConfigureAwait(true);

            // 退场：只淡出，不回弹
            _chip.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = FadeDuration,
                    Easing = new CubicEaseIn(),
                },
            };
            _chip.Opacity = 0;

            await Task.Delay(FadeDuration, token).ConfigureAwait(true);
            _chip.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // 被下一次 Show 抢占，交给新的播放流程
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Trigger 递增即播放一次；首次绑定（0→0）不触发
        if (change.Property == TriggerProperty && change.GetNewValue<int>() > 0)
        {
            Show();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
