using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Markdown.Avalonia;
using System.Text.RegularExpressions;
using WebViewControl;

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
    private MarkdownScrollViewer? _markdownViewer;
    private WebView? _webView;
    private TextBlock? _textBlock;
    private ContentFormat _currentFormat = ContentFormat.PlainText;

    public RichTextBlock()
    {
        InitializeComponent();
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
        var content = Text ?? string.Empty;
        var format = Format;

        if (format == ContentFormat.Auto)
        {
            format = DetectContentFormat(content);
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                RenderContent(content, format);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"富文本渲染错误: {ex.Message}");
                RenderAsPlainText(content);
            }
        });
    }

    /// <summary>
    /// 自动检测内容格式
    /// </summary>
    private ContentFormat DetectContentFormat(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ContentFormat.PlainText;

        var trimmedContent = content.Trim();

        // 检测HTML（更严格的判断）
        if (IsHtmlContent(trimmedContent))
            return ContentFormat.Html;

        // 检测Markdown（常见语法）
        if (IsMarkdownContent(trimmedContent))
            return ContentFormat.Markdown;

        return ContentFormat.PlainText;
    }

    /// <summary>
    /// 判断是否为HTML内容
    /// </summary>
    private bool IsHtmlContent(string content)
    {
        // 检查是否以HTML标签开始和结束
        if (content.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            content.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            return true;

        // 检查是否包含常见的HTML结构标签
        var htmlStructureTags = new[] { "<html", "<head", "<body", "<div", "<span", "<p>", "<br>", "<br/>" };
        var lowerContent = content.ToLowerInvariant();

        int htmlTagCount = 0;
        foreach (var tag in htmlStructureTags)
        {
            if (lowerContent.Contains(tag))
                htmlTagCount++;
        }

        // 如果包含多个HTML标签，认为是HTML
        if (htmlTagCount >= 2)
            return true;

        // 检查是否包含HTML实体
        if (Regex.IsMatch(content, @"&[a-z]+;|&#\d+;", RegexOptions.IgnoreCase))
            return true;

        return false;
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
    /// 使用HTML渲染
    /// </summary>
    private void RenderAsHtml(string htmlContent)
    {
        if (_webView == null)
        {
            _webView = new WebView
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

        _webView.LoadHtml(fullHtml);
    }

    /// <summary>
    /// 使用Markdown渲染
    /// </summary>
    private void RenderAsMarkdown(string markdownContent)
    {
        if (_markdownViewer == null)
        {
            _markdownViewer = new MarkdownScrollViewer();
            _markdownViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            _markdownViewer.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            _contentContainer!.Content = _markdownViewer;
        }

        _markdownViewer.Markdown = markdownContent;
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
    /// 清理当前查看器
    /// </summary>
    private void CleanupCurrentViewer()
    {
        if (_webView != null)
        {
            _webView.Dispose();
            _webView = null;
        }

        _markdownViewer = null;
        _textBlock = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CleanupCurrentViewer();
    }
}

