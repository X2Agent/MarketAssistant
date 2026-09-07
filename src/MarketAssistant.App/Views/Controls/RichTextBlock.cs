using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MarketAssistant.ViewModels;
using MdaMarkdown = Markdown.Avalonia.Markdown;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 内容格式类型
/// </summary>
public enum ContentFormat
{
    /// <summary>
    /// 自动检测
    /// </summary>
    Auto,
    /// <summary>
    /// 纯文本
    /// </summary>
    PlainText,
    /// <summary>
    /// Markdown格式
    /// </summary>
    Markdown,
    /// <summary>
    /// HTML格式
    /// </summary>
    Html
}

/// <summary>
/// 智能富文本控件，支持自动识别和渲染多种格式（HTML、Markdown、纯文本）
/// </summary>
public class RichTextBlock : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<RichTextBlock, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<ContentFormat> FormatProperty =
        AvaloniaProperty.Register<RichTextBlock, ContentFormat>(nameof(Format), ContentFormat.Auto);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ContentFormat Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    private ContentControl? _contentContainer;
    private MdaMarkdown? _markdownEngine;
    private NativeWebView? _webView;
    private TextBlock? _textBlock;
    private ContentFormat _currentFormat = ContentFormat.PlainText;

    /// <summary>
    /// 流式输出期间 Text 以 token 为粒度高频变化，而 Markdown 全量重解析 + 可视树重建成本高，
    /// 逐 token 渲染会打满 UI 线程导致消息列表抖动；用防抖计时器把渲染合并到稳定间隙
    /// </summary>
    private static readonly TimeSpan RenderDebounceInterval = TimeSpan.FromMilliseconds(120);
    private readonly DispatcherTimer _renderDebounceTimer;
    private string _pendingRenderText = string.Empty;

    /// <summary>
    /// 已把当前渲染器锁定为 Markdown 后不再回退纯文本：
    /// 流式半成品会让格式检测在“文本 ↔ Markdown”间反复翻转，触发 TextBlock 与
    /// Markdown 渲染器整树互换（可见闪烁），Markdown 输出中段回退纯文本毫无收益
    /// </summary>
    private bool _markdownLocked;

    /// <summary>
    /// 当前监听状态变化的 DataContext（ChatMessageAdapter）
    /// </summary>
    private INotifyPropertyChanged? _observedAdapter;

    public RichTextBlock()
    {
        InitializeComponent();
        _renderDebounceTimer = new DispatcherTimer(RenderDebounceInterval, DispatcherPriority.Background, OnRenderDebounceTick);
        DataContextChanged += OnDataContextChangedInternal;
    }

    private void OnDataContextChangedInternal(object? sender, EventArgs e)
    {
        if (_observedAdapter is not null)
        {
            _observedAdapter.PropertyChanged -= OnAdapterPropertyChanged;
        }

        _observedAdapter = DataContext as INotifyPropertyChanged;
        if (_observedAdapter is not null)
        {
            _observedAdapter.PropertyChanged += OnAdapterPropertyChanged;
        }
    }

    /// <summary>
    /// 消息进入终态（发送完成/失败）时立即冲刷防抖中的待渲染文本：
    /// 否则最后一波内容会在流结束后延迟一个防抖周期才出现，表现为收尾时文字滞后补齐的闪烁
    /// </summary>
    private void OnAdapterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatMessageAdapter.Status) || DataContext is not ChatMessageAdapter message)
            return;

        if (message.Status is MessageStatus.Sent or MessageStatus.Failed)
        {
            FlushPendingRender();
        }
    }

    private void InitializeComponent()
    {
        _contentContainer = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Content = _contentContainer;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty || change.Property == FormatProperty)
        {
            UpdateContent();
        }
    }

    private void UpdateContent()
    {
        // 仅记录待渲染文本，实际渲染延迟到防抖计时器触发，合并流式期间的高频更新
        _pendingRenderText = Text ?? string.Empty;
        if (!_renderDebounceTimer.IsEnabled)
        {
            _renderDebounceTimer.Start();
        }
    }

    /// <summary>
    /// 立即渲染待渲染文本（用于流结束等需要消除防抖尾延迟的时机）
    /// </summary>
    public void FlushPendingRender()
    {
        if (!_renderDebounceTimer.IsEnabled)
            return;

        _renderDebounceTimer.Stop();
        RenderPendingText();
    }

    private void OnRenderDebounceTick(object? sender, EventArgs e)
    {
        _renderDebounceTimer.Stop();
        RenderPendingText();
    }

    private void RenderPendingText()
    {
        var content = _pendingRenderText;
        var format = Format;

        if (format == ContentFormat.Auto)
        {
            format = DetectContentFormat(content);

            // 单向锁定：一旦按 Markdown 渲染过，后续流式半成品不再回退纯文本，
            // 避免 TextBlock ↔ Markdown 渲染器整树互换造成的闪烁
            if (format == ContentFormat.Markdown)
            {
                _markdownLocked = true;
            }
            else if (_markdownLocked)
            {
                format = ContentFormat.Markdown;
            }
        }

        try
        {
            RenderContent(content, format);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"富文本渲染错误: {ex.Message}");
            RenderAsPlainText(content);
        }
    }

    /// <summary>
    /// 自动检测内容格式。
    /// 安全决策（2026-08-28）：不再自动识别 HTML。聊天内容来源（模型流式输出、Web 搜索
    /// 抓取的网页正文、MCP 工具返回的第三方内容）均不可信，自动走 WebView 渲染意味着
    /// 未经消毒的 HTML/脚本可被间接提示注入触发执行。显式设置 Format=Html 的分支保留，
    /// 但启用前必须先实现白名单消毒（如 AngleSharp）+ CSP，见 RenderAsHtml。
    /// </summary>
    private ContentFormat DetectContentFormat(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ContentFormat.PlainText;

        var trimmedContent = content.Trim();

        // 检测Markdown（常见语法）
        if (IsMarkdownContent(trimmedContent))
            return ContentFormat.Markdown;

        return ContentFormat.PlainText;
    }

    /// <summary>
    /// 判断是否为Markdown内容
    /// </summary>
    private bool IsMarkdownContent(string content)
    {
        // 检查常见的Markdown语法
        var markdownPatterns = new[]
        {
            @"^#{1,6}\s+",           // 标题
            @"\*\*[^*]+\*\*",        // 粗体
            @"__[^_]+__",            // 粗体
            @"\*[^*]+\*",            // 斜体
            @"_[^_]+_",              // 斜体
            @"^\s*[-*+]\s+",         // 无序列表
            @"^\s*\d+\.\s+",         // 有序列表
            @"`[^`]+`",              // 内联代码
            @"```",                  // 代码块
            @"^\s*>",                // 引用
            @"\[([^\]]+)\]\(([^)]+)\)" // 链接
        };

        foreach (var pattern in markdownPatterns)
        {
            if (Regex.IsMatch(content, pattern, RegexOptions.Multiline))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 根据格式渲染内容
    /// </summary>
    private void RenderContent(string content, ContentFormat format)
    {
        if (_currentFormat != format || _contentContainer == null)
        {
            _currentFormat = format;
            CleanupCurrentViewer();
        }

        switch (format)
        {
            case ContentFormat.Html:
                RenderAsHtml(content);
                break;
            case ContentFormat.Markdown:
                RenderAsMarkdown(content);
                break;
            default:
                RenderAsPlainText(content);
                break;
        }
    }

    /// <summary>
    /// 使用HTML渲染。
    /// ⚠ 安全限制（2026-08-28 决策）：自动识别已禁用，仅当调用方显式设置 Format=Html 时才可达。
    /// 当前实现把原始内容零消毒直接插值进模板交给 WebView 执行——启用前必须先实现
    /// 标签/属性白名单消毒（建议 AngleSharp）并注入 CSP：
    /// &lt;meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"&gt;
    /// </summary>
    private void RenderAsHtml(string htmlContent)
    {
        if (_webView == null)
        {
            _webView = new NativeWebView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _contentContainer!.Content = _webView;
        }

        // 如果不是完整的HTML文档，包装成完整的HTML
        string fullHtml = htmlContent;
        if (!htmlContent.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) &&
            !htmlContent.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            fullHtml = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            padding: 8px;
            margin: 0;
            font-size: 14px;
            line-height: 1.6;
        }}
        pre {{ 
            background: #f5f5f5; 
            padding: 8px; 
            border-radius: 4px;
            overflow-x: auto;
        }}
        code {{ 
            background: #f5f5f5; 
            padding: 2px 4px; 
            border-radius: 2px;
            font-family: 'Consolas', 'Monaco', monospace;
        }}
    </style>
</head>
<body>
{htmlContent}
</body>
</html>";
        }

        _webView.NavigateToString(fullHtml);
    }

    /// <summary>
    /// 使用Markdown渲染。
    /// 直接用 Markdown 引擎把文本转换成控件树放进 ContentControl，不引入
    /// MarkdownScrollViewer：后者内部自带一层 ScrollViewer，嵌在 ListBox 项里
    /// 既增加测量开销、又干扰外层消息区的滚动；引擎产物复用同一个 ContentControl 承载
    /// </summary>
    private void RenderAsMarkdown(string markdownContent)
    {
        if (_contentContainer == null)
            return;

        if (_markdownEngine is null)
        {
            _markdownEngine = new MdaMarkdown();
            // 裸引擎不自带 MarkdownScrollViewer 的默认观感（反射确认其默认为 Standard 样式、
            // 默认引擎即 Markdown.Avalonia.Markdown），补挂同款 Standard 样式保证观感一致，
            // 挂在本控件 Styles 上即可命中引擎产出并挂入本控件子树的全部元素
            Styles.Add(new global::Markdown.Avalonia.StyleCollections.MarkdownStyleStandard());
        }

        // 引擎每次解析产出全新控件树，靠 ContentControl.Content 原子替换，
        // 不存在“先清空再逐步填充”的中间态
        _contentContainer.Content = _markdownEngine.Transform(markdownContent);
    }

    /// <summary>
    /// 使用纯文本渲染
    /// </summary>
    private void RenderAsPlainText(string textContent)
    {
        if (_textBlock == null)
        {
            _textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap
            };
            _contentContainer!.Content = _textBlock;
        }

        _textBlock.Text = textContent;
    }

    /// <summary>
    /// 清理当前查看器，释放原生资源
    /// </summary>
    private void CleanupCurrentViewer()
    {
        // WebView 持有原生资源，必须 Dispose 避免内存泄漏
        if (_webView is IDisposable disposableWebView)
        {
            try { disposableWebView.Dispose(); }
            catch { /* 忽略 Dispose 异常 */ }
        }
        _webView = null;
        _markdownEngine = null;
        _textBlock = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _renderDebounceTimer.Stop();
        if (_observedAdapter is not null)
        {
            _observedAdapter.PropertyChanged -= OnAdapterPropertyChanged;
            _observedAdapter = null;
        }
        base.OnDetachedFromVisualTree(e);
        CleanupCurrentViewer();
    }
}

