using System.Text.RegularExpressions;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 内容格式探测。
///
/// 从 RichTextBlock 中抽出，供"选择哪种渲染器"的调用方共用：
/// 纯文本类内容走 StreamingTextReveal（可逐字模糊揭示），
/// 含 Markdown 语法或结构化卡片的内容仍走 RichTextBlock / 卡片渲染器。
/// </summary>
public static class ContentFormatDetector
{
    private static readonly Regex[] MarkdownPatterns =
    [
        new(@"^#{1,6}\s+", RegexOptions.Multiline),          // 标题
        new(@"\*\*[^*]+\*\*"),                               // 粗体
        new(@"__[^_]+__"),                                   // 粗体
        new(@"\*[^*]+\*"),                                   // 斜体
        new(@"_([^_]+)_"),                                   // 斜体
        new(@"^\s*[-*+]\s+", RegexOptions.Multiline),        // 无序列表
        new(@"^\s*\d+\.\s+", RegexOptions.Multiline),        // 有序列表
        new(@"`[^`]+`"),                                     // 内联代码
        new("```"),                                          // 代码块
        new(@"^\s*>", RegexOptions.Multiline),               // 引用
        new(@"\[([^\]]+)\]\(([^)]+)\)"),                     // 链接
    ];

    /// <summary>内容是否含 Markdown 语法。</summary>
    public static bool IsMarkdown(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        foreach (var pattern in MarkdownPatterns)
        {
            if (pattern.IsMatch(content))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 是否适合用流式揭示控件渲染：非空且不含 Markdown 语法。
    /// 逐字模糊只对纯文本成立；Markdown 需全量重解析，逐字会导致开销成倍放大。
    /// </summary>
    public static bool IsPlainText(string content)
        => !string.IsNullOrWhiteSpace(content) && !IsMarkdown(content);
}
