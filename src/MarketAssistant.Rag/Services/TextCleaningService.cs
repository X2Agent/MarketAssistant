using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 文本清洗服务
/// </summary>
public class TextCleaningService : ITextCleaningService
{
    private readonly ILogger<TextCleaningService> _logger;

    // 预编译的正则表达式 - 固定清洗规则
    private static readonly Regex MultiSpace = new(@"[\t\x0B\f ]{2,}", RegexOptions.Compiled);
    // "第"分支要求后随"页"：否则"第 3 季度"这类正文中带空格的数字写法会被整段误删；
    // Latin/页/共 前缀与数字的组合语义明确，保持原有宽匹配
    private static readonly Regex PageNumber = new(
        @"(?:^|\s)(?:Page|页|p\.|P\.)\s*\d+(?:\s*(?:of|\/|共|页|总)\s*\d+)?(?:\s|$)|(?:^|\s)第\s*\d+\s*页|(?:^|\s)共\s*\d+\s*页",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HyphenBreak = new(@"([A-Za-z])-\n([A-Za-z])", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChineseSpecialChars = new(@"[　\u3000]+", RegexOptions.Compiled);
    // 修复：排除换行符 \x0A 和回车符 \x0D，避免删除换行符
    private static readonly Regex ControlChars = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F]", RegexOptions.Compiled);

    // 新增的清洗规则
    private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
    // 仅匹配中国手机号语义（13-19 开头共 11 位），带前后边界断言。
    // 严禁使用通用 N 位数字模式：金融文档中任意长数字串（成交额、证券代码、订单号）会被整段删除。
    private static readonly Regex PhonePattern = new(@"(?<![\d,.])(?:\+?86[- ]?)?1[3-9]\d{9}(?![\d])", RegexOptions.Compiled);
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

    /// <summary>
    /// 无损归一化：仅做不改变任何语义字符的处理（Unicode 标准化、换行统一、控制字符、全角空格、
    /// 多余空白合并、空行规范）。RAG 摄取管线必须使用本方法——金融文档中的数字
    /// （成交额、证券代码、手机号、日期）承载核心语义，任何有损规则都可能产出错误投资结论。
    /// </summary>
    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("输入文本为空或仅包含空白字符");
            return string.Empty;
        }

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

            // 5. 合并多余空白
            text = MultiSpace.Replace(text, " ");

            // 6. 规范空行
            text = NormalizeEmptyLines(text);

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文本归一化过程中发生错误");
            throw new InvalidOperationException("文本归一化失败", ex);
        }
    }

    /// <summary>
    /// 有损去噪：在 <see cref="Normalize"/> 基础上移除页眉页脚/页码/URL/邮箱/电话。
    /// 仅适用于确定无需保留这些内容的通用文本，禁止用于金融文档摄取。
    /// </summary>
    public string Denoise(string? text)
    {
        text = Normalize(text);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 移除页眉页脚
        text = HeaderFooterPattern.Replace(text, "\n");

        // 修复英文断词
        text = HyphenBreak.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value);

        // 移除页码
        text = PageNumber.Replace(text, " ");

        // 移除URL
        text = UrlPattern.Replace(text, " ");

        // 移除邮箱
        text = EmailPattern.Replace(text, " ");

        // 移除电话号码（仅中国手机号语义，带边界断言）
        text = PhonePattern.Replace(text, " ");

        // 移除类规则以空格占位，结束后再次合并空白并规范空行
        text = MultiSpace.Replace(text, " ");
        text = NormalizeEmptyLines(text);

        return text;
    }

    public string Clean(string? text)
    {
        var originalLength = text?.Length ?? 0;
        var cleaned = Denoise(text);
        var finalLength = cleaned.Length;
        _logger.LogDebug("文本清洗完成，原始长度: {Original}，最终长度: {Length}，压缩比: {Ratio:P2}",
            originalLength, finalLength, originalLength == 0 ? 0 : 1.0 - (double)finalLength / originalLength);
        return cleaned;
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
        var hasValidContent = cleanedText.Any(c => char.IsLetterOrDigit(c) || c >= 0x4e00 && c <= 0x9fff);

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



