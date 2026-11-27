using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 流式文本"极速高斯模糊 → 清晰"揭示控件。
///
/// 设计意图：AI 回复逐 token 到达时，新出现的文字不应"啪"地整块跳出，而应带一个
/// 极短的由虚到实过渡，并让相邻单元错开一点时间，形成"字在流出来"的观感。
/// 这与打字机光标是两回事：文字立即可见（只是略模糊），所以不会拖慢阅读，
/// 也不会因为逐字插入而造成布局跳动。
///
/// 为什么按"单元"而不是"字符"切分：
///  - 中日韩文字按单字切分，符合逐字涌现的直觉；
///  - 拉丁文按单词切分，否则单词会被拆成逐个字母而失去可读性。
///
/// 性能边界（关键）：
///  - 只有"当前段落"参与动画；已写完的段落合成为一个静态 TextBlock，动画对象数量有界。
///  - 追加只处理增量：已存在的单元保留原块（只更新文本），仅本次新增的单元建块并播放揭示。
///    整段重建会把已显示的文字每隔一个 token 重新模糊一遍，既 O(n²) 又会持续抖动。
///  - 同一时刻最多 24 个单元处于过渡中，超出的直接清晰显示。
///  - 单个单元的模糊对象在动画结束后立即摘掉，不给每个块常驻一个位图滤镜。
///
/// 参数与 Resources/Styles/Motion.axaml 同源（经 <see cref="MotionTokens"/> 读取）：
///  - 单单元模糊消退 220ms，起始半径 4px；
///  - 相邻单元错开 35ms（规范的 30–50ms 区间中值）。
/// </summary>
public class StreamingTextReveal : Decorator
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StreamingTextReveal, string>(nameof(Text), string.Empty);

    /// <summary>是否启用揭示动效。关闭时退化为普通整段文本（尊重减少动效偏好）。</summary>
    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<StreamingTextReveal, bool>(nameof(IsAnimated), true);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    private static TimeSpan RevealDuration => MotionTokens.StreamReveal;
    private static TimeSpan StaggerInterval => MotionTokens.StreamStagger;
    private static double BlurFromRadius => MotionTokens.StreamBlurFromRadius;
    private const int MaxConcurrentAnimations = 24;

    /// <summary>当前段落超过该字符量时整体固化，避免单元数无界增长。</summary>
    private const int MaxLiveChars = 900;

    private readonly StackPanel _root;
    private readonly TextBlock _settledText;
    private readonly WrapPanel _livePanel;

    /// <summary>活动段落已创建的单元块，与 <see cref="_liveBuffer"/> 的切分结果逐项对应。</summary>
    private readonly List<TextBlock> _liveBlocks = [];

    private string _renderedText = string.Empty;
    private readonly StringBuilder _settledBuffer = new();
    private readonly StringBuilder _liveBuffer = new();

    private int _animatingCount;

    public StreamingTextReveal()
    {
        // 初始不可见：空 TextBlock 仍会占一整行高度，首段文字走活动面板时气泡顶部会多出一行空白，
        // 等到出现换行并入静态文本后内容又整体上跳一行
        _settledText = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _livePanel = new WrapPanel { Orientation = Orientation.Horizontal };

        _root = new StackPanel();
        _root.Children.Add(_settledText);
        _root.Children.Add(_livePanel);

        BindTypography(_settledText);

        Child = _root;
    }

    private void BindTypography(TextBlock target)
    {
        target.Bind(TextBlock.FontFamilyProperty, this.GetObservable(FontFamilyProperty));
        target.Bind(TextBlock.FontSizeProperty, this.GetObservable(FontSizeProperty));
        target.Bind(TextBlock.FontWeightProperty, this.GetObservable(FontWeightProperty));
        target.Bind(TextBlock.ForegroundProperty, this.GetObservable(ForegroundProperty));
        target.Bind(TextBlock.LineHeightProperty, this.GetObservable(LineHeightProperty));
        target.Bind(TextBlock.TextWrappingProperty, this.GetObservable(TextWrappingProperty));
    }

    /// <summary>行高：Decorator 没有该属性，这里为揭示控件单独暴露一个。</summary>
    public static readonly StyledProperty<double> LineHeightProperty =
        TextBlock.LineHeightProperty.AddOwner<StreamingTextReveal>();

    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>字号。Decorator 不带字体属性，故显式暴露，供 XAML 直接设置。</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<StreamingTextReveal>();

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>字重。走主题资源即可自动跟随暗色降档。</summary>
    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner<StreamingTextReveal>();

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>字体族。</summary>
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<StreamingTextReveal>();

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>前景色。</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<StreamingTextReveal>();

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>换行方式。聊天气泡需要 Wrap，默认也取 Wrap。</summary>
    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        TextBlock.TextWrappingProperty.AddOwner<StreamingTextReveal>();

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            UpdateText(change.GetNewValue<string>() ?? string.Empty);
        }
    }

    private void UpdateText(string newText)
    {
        // 非增量（清空 / 换消息 / 内容回退）整体替换，不播放动效
        if (!IsAnimated || newText.Length <= _renderedText.Length || !newText.StartsWith(_renderedText, StringComparison.Ordinal))
        {
            ResetTo(newText);
            return;
        }

        var delta = newText.AsSpan(_renderedText.Length).ToString();
        _renderedText = newText;

        // 段落写完（出现换行）：活动段落并入静态文本，只对换行之后的部分继续做揭示
        var lastBreak = delta.LastIndexOf('\n');
        if (lastBreak >= 0)
        {
            CommitLiveSegment();
            _settledBuffer.Append(delta, 0, lastBreak + 1);
            FlushSettled();
            delta = delta[(lastBreak + 1)..];
        }

        if (delta.Length == 0)
        {
            return;
        }

        _liveBuffer.Append(delta);
        SyncLivePanel();

        if (_liveBuffer.Length > MaxLiveChars)
        {
            CommitLiveSegment();
        }
    }

    private void ResetTo(string text)
    {
        _renderedText = text;
        Volatile.Write(ref _animatingCount, 0);
        _settledBuffer.Clear();
        _liveBuffer.Clear();
        ClearLivePanel();

        _settledText.Text = string.Empty;
        _settledText.IsVisible = false;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _settledText.Text = text;
        _settledText.IsVisible = true;
    }

    private void EnterAnimation() => Interlocked.Increment(ref _animatingCount);

    private void LeaveAnimation()
    {
        // ResetTo 会把计数写回 0，此时旧动画的递减不能把它压成负数
        if (Interlocked.Decrement(ref _animatingCount) < 0)
        {
            Interlocked.Exchange(ref _animatingCount, 0);
        }
    }

    /// <summary>把当前活动段落并入静态缓冲区，并清空活动面板。</summary>
    private void CommitLiveSegment()
    {
        if (_liveBuffer.Length > 0)
        {
            _settledBuffer.Append(_liveBuffer);
            _liveBuffer.Clear();
        }

        ClearLivePanel();
        FlushSettled();
    }

    private void ClearLivePanel()
    {
        _livePanel.Children.Clear();
        _liveBlocks.Clear();
        _livePanel.IsVisible = false;
    }

    private void FlushSettled()
    {
        _settledText.Text = _settledBuffer.ToString();
        _settledText.IsVisible = _settledText.Text.Length > 0;
    }

    /// <summary>
    /// 让活动面板与 <see cref="_liveBuffer"/> 的最新切分结果对齐：
    /// 已有块只更新文本（末尾的拉丁单词会随输入继续变长），只有新增的块才实例化并播放揭示。
    /// </summary>
    private void SyncLivePanel()
    {
        var units = SplitIntoUnits(_liveBuffer.ToString());
        _livePanel.IsVisible = units.Count > 0;

        // 1. 复用：切分结果的前 N 项若已存在，只改文本
        var reused = Math.Min(units.Count, _liveBlocks.Count);
        for (var i = 0; i < reused; i++)
        {
            if (!string.Equals(_liveBlocks[i].Text, units[i], StringComparison.Ordinal))
            {
                _liveBlocks[i].Text = units[i];
            }
        }

        // 2. 收缩：切分结果变短（如末尾空白被合并）时移除多余块
        if (_liveBlocks.Count > units.Count)
        {
            for (var i = units.Count; i < _liveBlocks.Count; i++)
            {
                _livePanel.Children.Remove(_liveBlocks[i]);
            }

            _liveBlocks.RemoveRange(units.Count, _liveBlocks.Count - units.Count);
        }

        // 3. 追加：错峰计时从 0 起算，而不是按整段绝对下标，
        //    否则长段落新到达的字要等上好几秒才开始变清晰
        for (var i = _liveBlocks.Count; i < units.Count; i++)
        {
            AppendUnit(units[i], i - reused);
        }
    }

    private void AppendUnit(string unit, int staggerIndex)
    {
        var block = new TextBlock
        {
            Text = unit,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        BindTypography(block);

        _liveBlocks.Add(block);
        _livePanel.Children.Add(block);

        if (Volatile.Read(ref _animatingCount) >= MaxConcurrentAnimations)
        {
            // 超出过渡预算：直接清晰显示，避免一次性创建大量位图滤镜
            return;
        }

        EnterAnimation();
        _ = RevealAsync(block, StaggerInterval * staggerIndex);
    }

    /// <summary>
    /// 切分为布局单元：CJK 逐字、拉丁按词、空白单独成单元（保留词间空隙与换行机会）。
    /// </summary>
    private static List<string> SplitIntoUnits(string text)
    {
        var units = new List<string>(text.Length);
        var latin = new StringBuilder();

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                FlushLatin(units, latin);

                // 连续空白合并，避免产生大量空块
                if (units.Count > 0 && units[^1].Trim().Length == 0)
                {
                    units[^1] += rune.ToString();
                }
                else
                {
                    units.Add(rune.ToString());
                }

                continue;
            }

            if (IsCjkOrFullWidth(rune))
            {
                FlushLatin(units, latin);
                units.Add(rune.ToString());
                continue;
            }

            latin.Append(rune.ToString());

            // 后置标点之后断词，避免标点被推到下一行开头
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.OtherPunctuation or UnicodeCategory.ClosePunctuation)
            {
                FlushLatin(units, latin);
            }
        }

        FlushLatin(units, latin);
        return units;
    }

    private static void FlushLatin(List<string> units, StringBuilder latin)
    {
        if (latin.Length == 0)
        {
            return;
        }

        units.Add(latin.ToString());
        latin.Clear();
    }

    private static bool IsCjkOrFullWidth(Rune rune)
    {
        var value = rune.Value;
        return (value >= 0x3040 && value <= 0x30FF)   // 日文假名
            || (value >= 0x3400 && value <= 0x4DBF)   // 扩展 A
            || (value >= 0x4E00 && value <= 0x9FFF)   // 基本汉字
            || (value >= 0xF900 && value <= 0xFAFF)   // 兼容汉字
            || (value >= 0xFF00 && value <= 0xFFEF);  // 全角
    }

    private async Task RevealAsync(TextBlock target, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay).ConfigureAwait(true);
            }

            // 延时期间面板可能已重建，此时放弃这个已脱离可视树的单元
            if (!_livePanel.Children.Contains(target))
            {
                return;
            }

            var effect = new BlurEffect { Radius = BlurFromRadius };
            target.Effect = effect;
            target.Opacity = 0.35;

            // EffectTransition 继承自 Transition<IEffect>，Property 必须显式指定（不指定时
            // 在加入 Transitions 集合的瞬间抛 "Transition has no property specified"）。
            // 模糊半径是 BlurEffect 自身的属性，过渡要挂在 effect 上而不是 TextBlock 上：
            // 挂在 TextBlock 上时该 Transition 找不到对应属性，只会抛同样的异常。
            effect.Transitions = new Transitions
            {
                new DoubleTransition { Property = BlurEffect.RadiusProperty, Duration = RevealDuration, Easing = new CubicEaseOut() },
            };
            target.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = RevealDuration, Easing = new CubicEaseOut() },
            };

            effect.Radius = 0;
            target.Opacity = 1;

            await Task.Delay(RevealDuration).ConfigureAwait(true);

            // 动画结束即摘掉滤镜与过渡：半径已为 0，但留着 Effect 会让每个块常驻一个位图滤镜
            effect.Transitions = null;
            target.Transitions = null;
            target.Effect = null;
        }
        finally
        {
            LeaveAnimation();
        }
    }
}
