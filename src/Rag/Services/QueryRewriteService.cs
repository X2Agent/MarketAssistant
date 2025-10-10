using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 优化的查询改写服务，融合算法和启发式规则，不依赖大模型。
/// 针对金融投资场景深度优化，支持多维度智能扩展。
/// </summary>
public class QueryRewriteService : IQueryRewriteService
{
    private readonly ILogger<QueryRewriteService> _logger;

    // 合并的同义词词典（覆盖基础+热门词汇）
    private static readonly Dictionary<string, string[]> SynonymMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 基础金融词汇
        ["股票"] = ["证券", "股份", "股权", "个股", "股价"],
        ["价格"] = ["股价", "价值", "估值", "报价"],
        ["上涨"] = ["涨幅", "增长", "攀升", "走高", "上升"],
        ["下跌"] = ["跌幅", "下降", "回落", "走低", "下滑"],
        ["财报"] = ["年报", "季报", "业绩", "财务报告", "财务数据"],
        ["收益"] = ["利润", "盈利", "净利", "净利润"],
        ["营收"] = ["收入", "营业收入", "营业额"],
        ["市场"] = ["股市", "A股", "港股", "美股", "证券市场"],
        ["分析"] = ["研究", "评价", "评估", "解读"],
        ["投资"] = ["持仓", "配置", "买入", "建仓"],
        ["风险"] = ["波动", "不确定性", "风险点", "隐患"],
        ["趋势"] = ["走势", "方向", "态势", "发展"],

        // 热门行业词汇
        ["AI"] = ["人工智能", "机器学习", "深度学习"],
        ["新能源"] = ["电动车", "新能车", "锂电", "光伏", "风电"],
        ["芯片"] = ["半导体", "集成电路", "处理器"],
        ["医药"] = ["生物医药", "制药", "医疗"],
        ["房地产"] = ["地产", "房产"],

        // 宏观经济词汇
        ["降息"] = ["货币宽松", "降准"],
        ["加息"] = ["货币紧缩", "加息周期"],
        ["通胀"] = ["通货膨胀", "CPI上升"],
        ["GDP"] = ["经济增长", "国内生产总值"]
    };

    // 投资分析维度
    private static readonly string[] AnalysisDimensions =
    {
        "基本面", "技术面", "消息面", "估值", "风险", "催化剂", "政策面"
    };

    // 时间范围限定词
    private static readonly string[] TimeFrames =
    {
        "最新", "近一个月", "近三个月", "近半年", "近一年", "历史"
    };

    // 信息类型限定词
    private static readonly string[] InfoTypes =
    {
        "数据", "指标", "新闻", "公告", "研报", "财报", "分析"
    };

    // 停用词（简化版）
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "的", "了", "呢", "吗", "啊", "吧", "和", "与", "或", "但", "是", "在"
    };

    // 预编译正则表达式（简化版本）
    private static readonly Regex ChineseWordRegex = new(@"[\u4e00-\u9fa5]{2,}", RegexOptions.Compiled);

    public QueryRewriteService(ILogger<QueryRewriteService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogDebug("QueryRewriteService initialized");
    }

    public IReadOnlyList<string> Rewrite(string query, int maxCandidates = 3)
    {
        if (string.IsNullOrWhiteSpace(query) || maxCandidates <= 0)
        {
            return Array.Empty<string>();
        }

        try
        {
            _logger.LogDebug("Rewriting query: '{Query}', MaxCandidates: {MaxCandidates}", query, maxCandidates);

            var normalized = NormalizeQuery(query);
            var candidates = new List<string>();

            // 1. 同义词替换（优先级最高）
            foreach (var synonym in GenerateSynonymVariants(normalized))
            {
                candidates.Add(synonym);
                if (candidates.Count >= maxCandidates)
                    return DistinctKeepOrder(candidates);
            }

            // 2. 投资维度扩展
            foreach (var dimension in GenerateAnalysisDimensionVariants(normalized))
            {
                candidates.Add(dimension);
                if (candidates.Count >= maxCandidates)
                    return DistinctKeepOrder(candidates);
            }

            // 3. 时间范围扩展
            foreach (var timeframe in GenerateTimeFrameVariants(normalized))
            {
                candidates.Add(timeframe);
                if (candidates.Count >= maxCandidates)
                    return DistinctKeepOrder(candidates);
            }

            // 4. 信息类型扩展
            foreach (var infoType in GenerateInfoTypeVariants(normalized))
            {
                candidates.Add(infoType);
                if (candidates.Count >= maxCandidates)
                    return DistinctKeepOrder(candidates);
            }

            // 5. 关键词提取和重组（适度简化）
            foreach (var keywordVariant in GenerateKeywordVariants(normalized))
            {
                candidates.Add(keywordVariant);
                if (candidates.Count >= maxCandidates)
                    return DistinctKeepOrder(candidates);
            }

            // 6. 停用词移除的简化版本
            var compactQuery = RemoveStopWords(normalized);
            if (!string.Equals(compactQuery, normalized, StringComparison.Ordinal))
            {
                candidates.Add(compactQuery);
            }

            var result = DistinctKeepOrder(candidates).Take(maxCandidates).ToArray();

            _logger.LogInformation("Generated {Count} query variants for: '{Query}'", result.Length, query);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rewrite query: '{Query}'", query);
            return new[] { query };
        }
    }

    /// <summary>
    /// 标准化查询文本
    /// </summary>
    private static string NormalizeQuery(string query)
    {
        var s = query.Trim();
        // 清理常见的控制字符
        s = s.Replace("\r", "").Replace("\n", "").Replace("\t", " ");
        // 压缩多个空格为单个空格
        s = Regex.Replace(s, @"\s+", " ");
        return s;
    }

    /// <summary>
    /// 生成同义词变体
    /// </summary>
    private static IEnumerable<string> GenerateSynonymVariants(string query)
    {
        foreach (var kvp in SynonymMap)
        {
            if (query.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var synonym in kvp.Value)
                {
                    yield return query.Replace(kvp.Key, synonym, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    /// <summary>
    /// 生成分析维度变体
    /// </summary>
    private static IEnumerable<string> GenerateAnalysisDimensionVariants(string query)
    {
        foreach (var dimension in AnalysisDimensions)
        {
            yield return $"{query} {dimension}";
        }
    }

    /// <summary>
    /// 生成时间范围变体
    /// </summary>
    private static IEnumerable<string> GenerateTimeFrameVariants(string query)
    {
        foreach (var timeFrame in TimeFrames)
        {
            yield return $"{query} {timeFrame}";
        }
    }

    /// <summary>
    /// 生成信息类型变体
    /// </summary>
    private static IEnumerable<string> GenerateInfoTypeVariants(string query)
    {
        foreach (var infoType in InfoTypes)
        {
            yield return $"{query} {infoType}";
        }
    }

    /// <summary>
    /// 生成关键词变体（简化版本）
    /// </summary>
    private static IEnumerable<string> GenerateKeywordVariants(string query)
    {
        var keywords = ExtractKeywords(query);
        if (keywords.Count <= 1) yield break;

        // 只生成最重要的两两组合，避免过度复杂
        for (int i = 0; i < Math.Min(2, keywords.Count); i++)
        {
            for (int j = i + 1; j < Math.Min(3, keywords.Count); j++)
            {
                yield return $"{keywords[i]} {keywords[j]}";
            }
        }
    }

    /// <summary>
    /// 提取关键词（简化版本）
    /// </summary>
    private static List<string> ExtractKeywords(string query)
    {
        var keywords = new List<string>();

        // 提取中文词汇（至少2个字符）
        var chineseMatches = ChineseWordRegex.Matches(query);
        foreach (Match match in chineseMatches)
        {
            keywords.Add(match.Value);
        }

        // 提取英文单词和重要数字
        var words = query.Split([' ', '，', '。', '、'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var cleanWord = word.Trim('(', ')', '[', ']', '"', '\'');
            if (cleanWord.Length >= 2 &&
                (Regex.IsMatch(cleanWord, @"^[A-Za-z]+$") || // 英文单词
                 Regex.IsMatch(cleanWord, @"^\d{4}$"))) // 年份等重要数字
            {
                keywords.Add(cleanWord);
            }
        }

        // 按长度排序，长词优先
        return keywords.Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderByDescending(k => k.Length)
                      .Take(5) // 最多5个关键词，避免过度复杂
                      .ToList();
    }

    /// <summary>
    /// 移除停用词
    /// </summary>
    private static string RemoveStopWords(string query)
    {
        var words = query.Split([' ', '，', '。', '、'], StringSplitOptions.RemoveEmptyEntries);
        var filteredWords = words.Where(word => !StopWords.Contains(word)).ToArray();

        return string.Join(" ", filteredWords).Trim();
    }

    /// <summary>
    /// 去重并保持顺序
    /// </summary>
    private static IReadOnlyList<string> DistinctKeepOrder(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var candidate in candidates)
        {
            var trimmed = candidate.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
