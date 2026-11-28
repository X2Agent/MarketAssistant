using Avalonia;
using Avalonia.Controls;

namespace MarketAssistant.Views.Controls;

/// <summary>提示条语义，决定底色、文字色与图标。</summary>
public enum StatusSeverity
{
    /// <summary>中性说明（蓝）。</summary>
    Info,

    /// <summary>已完成 / 已满足（绿）。</summary>
    Success,

    /// <summary>需要留意但不阻断（琥珀）：输入校验、功能被关闭等。</summary>
    Warning,

    /// <summary>失败或高风险（红）：加载失败、实盘提醒、分析师失败等。</summary>
    Danger,
}

/// <summary>
/// 语义提示条：浅语义底 + 同色文字 + 同色图标，不描边。
///
/// <para>
/// <see cref="ContentControl.Content"/> 是右侧可选操作槽（如失败条里的「重试」按钮）；
/// 没有内容时不占位。
/// </para>
///
/// <para>
/// 页面里的提示条一律用本控件，不要再手写 Border + TextBlock：
/// 内边距、圆角、字号与图标由控件统一，避免各处各写一份后逐渐漂移。
/// </para>
/// </summary>
public class StatusBanner : ContentControl
{
    private static readonly string[] SeverityClasses =
        ["severity-info", "severity-success", "severity-warning", "severity-danger"];

    /// <summary>语义，默认中性说明。</summary>
    public static readonly StyledProperty<StatusSeverity> SeverityProperty =
        AvaloniaProperty.Register<StatusBanner, StatusSeverity>(nameof(Severity));

    /// <summary>提示正文，窄窗下自动换行。</summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<StatusBanner, string?>(nameof(Message));

    public StatusSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    static StatusBanner()
    {
        SeverityProperty.Changed.AddClassHandler<StatusBanner>((banner, _) => banner.ApplySeverity());
    }

    public StatusBanner()
    {
        ApplySeverity();
    }

    private void ApplySeverity()
    {
        // 用样式类而不是直接写 Background / Foreground：颜色必须跟随主题切换。
        foreach (var name in SeverityClasses)
        {
            Classes.Remove(name);
        }

        Classes.Add(SeverityClasses[(int)Severity]);
    }
}
