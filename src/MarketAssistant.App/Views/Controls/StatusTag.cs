using Avalonia;
using Avalonia.Controls.Primitives;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 状态标签：12% 语义底 + 配对文字色，不描边。
///
/// <para>
/// 语义用样式类切换，默认中性；可选 <c>info / success / warning / danger / bullish / bearish</c>。
/// 动态语义直接绑定 <c>Classes.xxx</c>，如 <c>Classes.warning="{Binding IsTestnet}"</c>。
/// 同时带多个语义类时声明靠后的生效（顺序见 StatusTag.axaml）。
/// </para>
///
/// <para>
/// 页面里的标签一律用本控件，不要再手写 Border + TextBlock：
/// 字色必须用配对令牌，直接拿 WarningBrush / ErrorBrush 压在 12% 色底上，
/// 浅色下只有 2.8 / 4.0，12px 小字不达 AA。
/// </para>
/// </summary>
public class StatusTag : TemplatedControl
{
    /// <summary>标签文字。</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusTag, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
