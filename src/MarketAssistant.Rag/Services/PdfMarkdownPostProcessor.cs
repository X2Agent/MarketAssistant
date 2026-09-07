using System.Text;
using System.Text.RegularExpressions;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// Markdown 输出后处理（internal 静态类）：压缩空行、规范化标题/列表/段落间距。
/// </summary>
internal static class PdfMarkdownPostProcessor
{
    internal static string PostProcessMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;

        // 移除过多的空行
        markdown = PdfTextUtility.MultipleNewlinesRegex.Replace(markdown, "\n\n");

        // 修复格式问题
        var lines = markdown.Split('\n');
        var result = new StringBuilder();
        var previousLineWasEmpty = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // 标题前后确保有空行
            if (trimmedLine.StartsWith('#'))
            {
                if (!previousLineWasEmpty && result.Length > 0)
                {
                    result.AppendLine();
                }
                result.AppendLine(trimmedLine);
                result.AppendLine();
                previousLineWasEmpty = true;
            }
            // 列表项
            else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("1. ") ||
                     Regex.IsMatch(trimmedLine, @"^\s*\d+\.\s"))
            {
                result.AppendLine(trimmedLine);
                previousLineWasEmpty = false;
            }
            // 空行
            else if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                if (!previousLineWasEmpty)
                {
                    result.AppendLine();
                    previousLineWasEmpty = true;
                }
            }
            // 普通内容
            else
            {
                result.AppendLine(trimmedLine);
                previousLineWasEmpty = false;
            }
        }

        return result.ToString().Trim();
    }
}
