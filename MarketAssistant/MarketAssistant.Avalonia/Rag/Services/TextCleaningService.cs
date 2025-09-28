using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 文本清洗服务
/// </summary>
public class TextCleaningService : ITextCleaningService
{
    private readonly ILogger<TextCleaningService> _logger;

    // 预编译的正则表达式 - 固定清洗规则
    private static readonly Regex MultiSpace = new(@"[\t\x0B\f ]{2,}", RegexOptions.Compiled);
    private static readonly Regex PageNumber = new(
        @"(?:^|\s)(?:Page|页|第|p\.|P\.)\s*\d+(?:\s*(?:of|\/|共|页|总)\s*\d+)?(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HyphenBreak = new(@"([A-Za-z])-\n([A-Za-z])", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChineseSpecialChars = new(@"[　\u3000]+", RegexOptions.Compiled);
    // 修复：排除换行符 \x0A 和回车符 \x0D，避免删除换行符
    private static readonly Regex ControlChars = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F]", RegexOptions.Compiled);

    // 新增的清洗规则
    private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"(?:\+?1[-.\s]?)?\(?([0-9]{3})\)?[-.\s]?([0-9]{3})[-.\s]?([0-9]{4})", RegexOptions.Compiled);
    private static readonly Regex ChinesePhonePattern = new(@"1[3-9]\d{9}", RegexOptions.Compiled);
    private static readonly Regex RepeatingChars = new(@"(.)\1{3,}", RegexOptions.Compiled); // 连续重复字符
    private static readonly Regex HeaderFooterPattern = new(@"(?:^|\n)(?:Header|Footer|页眉|页脚):.*?(?:\n|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TextCleaningService(ILogger<TextCleaningService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 用于测试的无参构造函数
    /// </summary>
    public TextCleaningService() : this(Microsoft.Extensions.Logging.Abstractions.NullLogger<TextCleaningService>.Instance)
    {
    }

    public string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("输入文本为空或仅包含空白字符");
            return string.Empty;
        }

        var originalLength = text.Length;
        _logger.LogDebug("开始清洗文本，原始长度: {Length}", originalLength);

        try
        {
            // 1. Unicode标准化
            text = text.Normalize(NormalizationForm.FormC);

            // 2. 统一换行符
            text = NormalizeLineEndings(text);

            // 3. 移除控制字符
            text = ControlChars.Replace(text, "");

            // 4. 处理中文全角空格
            text = ChineseSpecialChars.Replace(text, " ");

            // 5. 移除页眉页脚
            text = HeaderFooterPattern.Replace(text, "\n");

            // 6. 修复英文断词
            text = HyphenBreak.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value);

            // 7. 移除页码
            text = PageNumber.Replace(text, " ");

            // 8. 移除URL
            text = UrlPattern.Replace(text, " ");

            // 9. 移除邮箱
            text = EmailPattern.Replace(text, " ");

            // 10. 移除电话号码
            text = PhonePattern.Replace(text, " ");
            text = ChinesePhonePattern.Replace(text, " ");

            // 11. 处理重复字符
            text = RepeatingChars.Replace(text, "$1$1");

            // 12. 合并多余空白
            text = MultiSpace.Replace(text, " ");

            // 13. 规范空行
            text = NormalizeEmptyLines(text);

            var finalLength = text.Length;
            _logger.LogDebug("文本清洗完成，最终长度: {Length}，压缩比: {Ratio:P2}",
                finalLength, 1.0 - (double)finalLength / originalLength);

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文本清洗过程中发生错误");
            throw new InvalidOperationException("文本清洗失败", ex);
        }
    }

    /// <summary>
    /// 验证清洗结果是否可接受
    /// </summary>
    public bool IsCleaningSuccessful(string originalText, string cleanedText)
    {
        // 简单的成功标准：
        // 1. 清洗后不为空
        // 2. 保留了基本内容（至少50%的有效字符）
        // 3. 压缩率不超过70%（避免过度删除）

        if (string.IsNullOrWhiteSpace(cleanedText)) return false;
        if (string.IsNullOrWhiteSpace(originalText)) return true;

        var compressionRatio = 1.0 - (double)cleanedText.Length / originalText.Length;
        var hasValidContent = cleanedText.Any(c => char.IsLetterOrDigit(c) || (c >= 0x4e00 && c <= 0x9fff));

        return compressionRatio <= 0.7 && hasValidContent;
    }

    /// <summary>
    /// 统一换行符
    /// </summary>
    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>
    /// 规范空行处理 - 最多保留1个空行
    /// </summary>
    private static string NormalizeEmptyLines(string text)
    {
        var lines = text.Split('\n');
        var result = new List<string>();
        var emptyCount = 0;
        const int maxEmptyLines = 1;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                emptyCount++;
                if (emptyCount <= maxEmptyLines)
                {
                    result.Add("");
                }
            }
            else
            {
                emptyCount = 0;
                result.Add(trimmed);
            }
        }

        // 使用 String.Join 保留换行符结构
        var joinedResult = string.Join('\n', result);

        // 移除开头和结尾的空行
        return joinedResult.Trim('\n');
    }
}



