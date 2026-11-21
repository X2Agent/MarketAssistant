using Avalonia;
using Avalonia.Styling;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// Motion.axaml 动效令牌的代码侧读取器。
/// 数值单一来源在 Resources/Styles/Motion.axaml；读取失败（设计器预览、
/// 单元测试等无 App 资源环境）时回退内置默认值，保证控件始终可用。
/// </summary>
public static class MotionTokens
{
    private static double Get(string key, double fallback) =>
        Application.Current?.TryGetResource(key, ThemeVariant.Default, out var value) == true &&
        value is double d
            ? d
            : fallback;

    /// <summary>流式文本：单单元"模糊→清晰"时长（ms）。</summary>
    public static TimeSpan StreamReveal => TimeSpan.FromMilliseconds(Get("StreamRevealMs", 220));

    /// <summary>流式文本：相邻单元错开间隔（ms）。</summary>
    public static TimeSpan StreamStagger => TimeSpan.FromMilliseconds(Get("StreamStaggerMs", 35));

    /// <summary>流式文本：起始模糊半径（px）。</summary>
    public static double StreamBlurFromRadius => Get("StreamBlurFromRadius", 4);

    /// <summary>复制反馈：对勾停留时长（ms）。</summary>
    public static TimeSpan CopyFeedbackHold => TimeSpan.FromMilliseconds(Get("CopyFeedbackHoldMs", 2500));

    /// <summary>复制反馈：对勾淡出时长（ms）。</summary>
    public static TimeSpan CopyFeedbackFade => TimeSpan.FromMilliseconds(Get("CopyFeedbackFadeMs", 180));

    /// <summary>开关圆点：位移途中拉伸成胶囊的时长（ms）。</summary>
    public static TimeSpan SwitchStretch => TimeSpan.FromMilliseconds(Get("SwitchStretchMs", 90));

    /// <summary>开关圆点：到点后回落成圆形的时长（ms，带过冲回弹）。</summary>
    public static TimeSpan SwitchSettle => TimeSpan.FromMilliseconds(Get("SwitchSettleMs", 200));

    /// <summary>开关圆点：位移途中的横向拉伸倍率。</summary>
    public static double KnobStretchScaleX => Get("KnobStretchScaleX", 1.25);

    /// <summary>开关圆点：位移途中的纵向压缩倍率。</summary>
    public static double KnobStretchScaleY => Get("KnobStretchScaleY", 0.90);

    /// <summary>收藏烟花：总时长（ms）。</summary>
    public static TimeSpan BurstDuration => TimeSpan.FromMilliseconds(Get("BurstDurationMs", 520));

    /// <summary>收藏烟花：粒子数。</summary>
    public static int BurstParticleCount => (int)Get("BurstParticleCount", 12);
}
