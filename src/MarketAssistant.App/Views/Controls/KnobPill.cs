using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 弹性开关的圆点：位移途中拉成胶囊，到点后回落成圆形。
///
/// 动效性格 D（物理/动量），见 Motion.axaml：
///  - 位置插值由 <see cref="ToggleSwitch.KnobTransitions"/> 负责，**不做位置过冲**
///    （否则圆点会跑出 40px 轨道）；
///  - "回弹"落在形体上：横向拉伸 <see cref="MotionTokens.KnobStretchScaleX"/>、
///    纵向压扁 <see cref="MotionTokens.KnobStretchScaleY"/>，随后用 BackEaseOut 落回圆形，
///    过冲 ≈12% 由缓动函数给出。
///
/// 以**自身中心**为变换原点：16px 圆点 ×1.25 = 20px，正好填满 20px 的圆点行程格，
/// 拉伸时既不会越出轨道，也不需要给轨道加裁剪（矩形裁剪会在两端把圆点削平）。
///
/// 只在"非空 → 非空"的真实切换时播放：首次绑定（null → true）不播，
/// 否则列表里一次性创建的多个开关会同时抽动一下，像页面在错误抖动。
/// </summary>
public class KnobPill : Border
{
    /// <summary>开关当前状态（由 ToggleSwitch 的 IsChecked 模板绑定）。</summary>
    public static readonly StyledProperty<bool?> IsOnProperty =
        AvaloniaProperty.Register<KnobPill, bool?>(nameof(IsOn));

    public bool? IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    private static readonly TransformOperations Identity = TransformOperations.Parse("scale(1,1)");

    private CancellationTokenSource? _cts;
    private bool _isInitialized;

    public KnobPill()
    {
        CornerRadius = new CornerRadius(8);
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = Identity;
    }

    static KnobPill()
    {
        IsOnProperty.Changed.AddClassHandler<KnobPill>((pill, change) => pill.OnIsOnChanged(change));
    }

    private void OnIsOnChanged(AvaloniaPropertyChangedEventArgs change)
    {
        var oldValue = change.GetOldValue<bool?>();
        var newValue = change.GetNewValue<bool?>();

        // 首次绑定 / 被数据重置为 null：只落位，不播放
        if (!_isInitialized)
        {
            _isInitialized = true;
            return;
        }

        if (oldValue is null || newValue is null)
        {
            return;
        }

        Play();
    }

    private void Play()
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
            Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = MotionTokens.SwitchStretch,
                    Easing = new CubicEaseOut(),
                },
            };

            RenderTransform = TransformOperations.Parse(
                FormattableString.Invariant(
                    $"scale({MotionTokens.KnobStretchScaleX:0.##},{MotionTokens.KnobStretchScaleY:0.##})"));

            await Task.Delay(MotionTokens.SwitchStretch, token).ConfigureAwait(true);

            // 到点回落：BackEaseOut 先冲过 1 再收回，即"轻微回弹"
            Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = MotionTokens.SwitchSettle,
                    Easing = new BackEaseOut(),
                },
            };

            RenderTransform = Identity;
        }
        catch (OperationCanceledException)
        {
            // 被下一次切换抢占：直接落回圆形，交给新的播放流程
            Transitions = null;
            RenderTransform = Identity;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
