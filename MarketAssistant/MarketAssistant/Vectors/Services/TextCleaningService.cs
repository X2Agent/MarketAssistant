using MarketAssistant.Vectors.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 文本清洗默认实现。
/// </summary>
public class TextCleaningService : ITextCleaningService
{
    // 常见页眉/页脚/页码与冗余噪声模式
    static readonly Regex MultiSpace = new Regex("[\t\x0B\f\r ]{2,}", RegexOptions.Compiled);
    // 可以考虑更精确的页码模式，避免误删有用数字
    static readonly Regex PageNumber = new Regex(
        @"(?:^|\s)(?:Page|页|第)\s*\d+(?:\s*(?:of|\/|共|页)\s*\d+)?(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex HyphenBreak = new Regex(@"([A-Za-z])-\n([A-Za-z])", RegexOptions.Compiled); // 英文断词换行
    static readonly Regex UrlLike = new Regex("https?://\\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // 统一换行
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 修复英文断词
        text = HyphenBreak.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value);

        // 移除页码/冗余链接
        text = PageNumber.Replace(text, " ");
        text = UrlLike.Replace(text, " ");

        // 合并多余空白
        text = MultiSpace.Replace(text, " ");

        // 修剪与规范空行（最多保留1个空行）
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var emptyCount = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                emptyCount++;
                if (emptyCount > 1) continue;
                sb.Append('\n');
                continue;
            }
            emptyCount = 0;
            sb.Append(trimmed).Append('\n');
        }

        return sb.ToString().Trim();
    }
}



