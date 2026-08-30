using System.Text.RegularExpressions;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF→Markdown 转换管线共用的编译正则、文本清洗与标题阈值常量。
/// 仅供本程序集内的转换器与各辅助类使用（internal）。
/// </summary>
internal static class PdfTextUtility
{
    // 编译的正则表达式，提高性能
    internal static readonly Regex MultipleSpacesRegex = new(@"\s{2,}", RegexOptions.Compiled);
    internal static readonly Regex MultipleNewlinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
    internal static readonly Regex NumberedListRegex = new(@"^\s*(\d+\.|\d+\)|\(\d+\))\s+", RegexOptions.Compiled);
    internal static readonly Regex BulletListRegex = new(@"^\s*[•\-\*\◦\▪\▫]\s+", RegexOptions.Compiled);
    internal static readonly Regex ChapterNumberRegex = new(@"^\s*(第[一二三四五六七八九十\d]+章|Chapter\s+\d+|CHAPTER\s+\d+)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    internal static readonly Regex SectionNumberRegex = new(@"^\s*(\d+(\.\d+)*)\s+", RegexOptions.Compiled);
    internal static readonly Regex ChineseSectionRegex = new(@"^\s*([一二三四五六七八九十]|[1-9]\d*)、", RegexOptions.Compiled);
    internal static readonly Regex ChineseSubSectionRegex = new(@"^\s*（([一二三四五六七八九十]|[1-9]\d*)）", RegexOptions.Compiled);

    // 标题字体大小阈值
    internal const double LargeHeadingThreshold = 1.5;
    internal const double MediumHeadingThreshold = 1.3;
    internal const double SmallHeadingThreshold = 1.1;

    internal static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 移除多余的空格
        text = MultipleSpacesRegex.Replace(text, " ");

        // 处理常见的PDF编码问题
        text = text
            .Replace("ﬁ", "fi")     // 连字符修复
            .Replace("ﬂ", "fl")
            .Replace("ﬀ", "ff")
            .Replace("ﬃ", "ffi")
            .Replace("ﬄ", "ffl")
            .Replace("–", "-")      // 短破折号
            .Replace("—", "--")     // 长破折号
            .Replace("\u201C", "\"") // 左双引号
            .Replace("\u201D", "\"") // 右双引号
            .Replace("\u2018", "'")  // 左单引号
            .Replace("\u2019", "'")  // 右单引号
            .Replace("\u2013", "-")  // 短破折号
            .Replace("\u2014", "--") // 长破折号
            .Replace("\u00A0", " ")  // 非断行空格
            .Trim();

        return text;
    }

    internal static bool IsChinese(char c)
    {
        // 检查是否为中文字符（CJK统一汉字）
        return c >= 0x4E00 && c <= 0x9FFF ||  // CJK Unified Ideographs
               c >= 0x3400 && c <= 0x4DBF ||  // CJK Extension A
               c >= 0x20000 && c <= 0x2A6DF;  // CJK Extension B
    }
}
