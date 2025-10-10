using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Data;
using System.Text.RegularExpressions;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 词元信息
/// </summary>
public record TokenInfo(string Token, double Bonus, int Frequency = 1);

/// <summary>
/// 评分权重配置
/// </summary>
public record ScoringWeights
{
    public double Relevance { get; init; } = 0.55;
    public double Freshness { get; init; } = 0.25;
    public double Length { get; init; } = 0.20;
}

/// <summary>
/// 评分计算常量
/// </summary>
public record ScoringConstants
{
    public double ExactMatchBonus { get; init; } = 0.2;
    public double HighSimilarityThreshold { get; init; } = 0.7;
    public double SimilarityPenalty { get; init; } = 0.8;
    public int IdealLengthMin { get; init; } = 200;
    public int IdealLengthMax { get; init; } = 1000;
    public int CjkMinGram { get; init; } = 2;
    public int CjkMaxGram { get; init; } = 3;
}

/// <summary>
/// 评分结果
/// </summary>
public class ScoredResult
{
    public TextSearchResult Item { get; init; } = null!;
    public double RelevanceScore { get; init; }
    public double FreshnessScore { get; init; }
    public double LengthScore { get; init; }
    public double TotalScore { get; set; }

    public ScoredResult(TextSearchResult item)
    {
        Item = item;
    }
}

/// <summary>
/// 重排序服务 - 基于启发式算法，专为金融场景优化
/// 多维度评分：文本相关性 + 时效性 + 长度优化 + 多样性
/// </summary>
public class RerankerService : IRerankerService
{
    private readonly ILogger<RerankerService> _logger;
    private static readonly ScoringWeights Weights = new();
    private static readonly ScoringConstants Constants = new();

    public RerankerService(ILogger<RerankerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<TextSearchResult> Rerank(
        string query,
        IEnumerable<TextSearchResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            _logger.LogDebug("重排序输入为空，直接返回");
            return itemList;
        }

        var safeQuery = query?.Trim() ?? string.Empty;
        _logger.LogDebug("开始启发式重排序，结果数量: {Count}, 查询: '{Query}'", itemList.Count, safeQuery);

        try
        {
            // 预处理查询词元
            var queryTokens = TokenizeWithBonus(safeQuery);

            // 计算各项评分
            var scoredResults = CalculateScores(itemList, queryTokens, safeQuery);

            // 应用多样性优化
            ApplyDiversityOptimization(scoredResults);

            // 排序并返回结果
            var rankedResults = scoredResults
                .OrderByDescending(r => r.TotalScore)
                .Select(r => r.Item)
                .ToList();

            LogTopResults(scoredResults);
            return rankedResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重排序过程中发生错误，查询: '{Query}'", safeQuery);
            // 发生错误时返回原始排序
            return itemList;
        }
    }

    #region 评分计算核心方法

    private List<ScoredResult> CalculateScores(List<TextSearchResult> items, IReadOnlyList<TokenInfo> queryTokens, string query)
    {
        return items.Select(item => CalculateItemScore(item, queryTokens, query)).ToList();
    }

    private ScoredResult CalculateItemScore(TextSearchResult item, IReadOnlyList<TokenInfo> queryTokens, string query)
    {
        var text = GetFullText(item);
        var itemTokens = TokenizeWithBonus(text);

        var scores = new
        {
            Relevance = CalculateRelevanceScore(queryTokens, itemTokens, query, text),
            Freshness = CalculateFreshnessScore(item),
            Length = CalculateLengthScore(text)
        };

        var totalScore = Weights.Relevance * scores.Relevance +
                        Weights.Freshness * scores.Freshness +
                        Weights.Length * scores.Length;

        return new ScoredResult(item)
        {
            RelevanceScore = scores.Relevance,
            FreshnessScore = scores.Freshness,
            LengthScore = scores.Length,
            TotalScore = totalScore
        };
    }

    #endregion

    #region 评分算法实现

    /// <summary>
    /// 计算相关性评分
    /// </summary>
    private static double CalculateRelevanceScore(IReadOnlyList<TokenInfo> queryTokens, IReadOnlyList<TokenInfo> itemTokens, string query, string text)
    {
        if (queryTokens.Count == 0) return 0.0;

        var itemTokenDict = itemTokens.ToDictionary(t => t.Token, t => t, StringComparer.OrdinalIgnoreCase);
        var queryBonusSum = queryTokens.Sum(t => t.Bonus);

        double totalScore = 0.0;
        int matchedTokens = 0;

        foreach (var qToken in queryTokens)
        {
            if (itemTokenDict.TryGetValue(qToken.Token, out var matchingItem))
            {
                matchedTokens++;
                totalScore += qToken.Bonus * matchingItem.Frequency;
            }
        }

        var baseScore = (double)matchedTokens / queryTokens.Count;
        var weightedScore = queryBonusSum > 0 ? totalScore / queryBonusSum : 0.0;
        var exactMatchBonus = text.Contains(query, StringComparison.OrdinalIgnoreCase) ? Constants.ExactMatchBonus : 0.0;

        return Math.Min(1.0, 0.6 * baseScore + 0.3 * weightedScore + 0.1 + exactMatchBonus);
    }

    /// <summary>
    /// 计算时效性评分
    /// </summary>
    private static double CalculateFreshnessScore(TextSearchResult item)
    {
        var uri = item.Link ?? string.Empty;
        var text = item.Value ?? string.Empty;

        var urlScore = ExtractDateFromUrl(uri);
        if (urlScore > 0) return urlScore;

        var contentScore = ExtractDateFromContent(text);
        return contentScore > 0 ? contentScore : 0.5;
    }

    /// <summary>
    /// 计算长度评分
    /// </summary>
    private static double CalculateLengthScore(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0.0;

        var length = text.Length;
        return length switch
        {
            >= 200 and <= 1000 => 1.0,
            >= 100 and <= 1500 => 0.8,
            >= 50 and <= 2000 => 0.6,
            < 50 => 0.3,
            _ => Math.Max(0.2, 1.0 - (length - 2000) / 10000.0)
        };
    }

    #endregion

    #region 多样性优化

    /// <summary>
    /// 应用多样性优化
    /// </summary>
    private static void ApplyDiversityOptimization(List<ScoredResult> results)
    {
        if (results.Count <= 1) return;

        var tokenSetCache = new Dictionary<ScoredResult, HashSet<string>>();
        foreach (var result in results)
        {
            var text = GetFullText(result.Item);
            var tokens = TokenizeWithBonus(text)
                .Select(t => t.Token)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            tokenSetCache[result] = tokens;
        }

        for (int i = 0; i < results.Count; i++)
        {
            var current = results[i];
            var diversityScore = 1.0;
            var tokensI = tokenSetCache[current];

            for (int j = 0; j < i; j++)
            {
                var tokensJ = tokenSetCache[results[j]];
                var similarity = CalculateJaccardSimilarity(tokensI, tokensJ);
                if (similarity > Constants.HighSimilarityThreshold)
                {
                    diversityScore *= Constants.SimilarityPenalty;
                }
            }

            current.TotalScore *= diversityScore;
        }
    }

    /// <summary>
    /// 计算Jaccard相似度
    /// </summary>
    private static double CalculateJaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;
        int intersection = a.Intersect(b).Count();
        int union = a.Count + b.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    #endregion

    #region 分词和关键词处理

    /// <summary>
    /// 带权重的分词处理
    /// </summary>
    private static List<TokenInfo> TokenizeWithBonus(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<TokenInfo>();

        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"[^\p{L}\p{N}]+", " ");
        var rawTokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var tokenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in rawTokens)
        {
            if (ContainsCjk(token))
            {
                foreach (var gram in GenerateCjkNgrams(token))
                {
                    if (!string.IsNullOrEmpty(gram))
                    {
                        tokenCounts[gram] = tokenCounts.GetValueOrDefault(gram) + 1;
                    }
                }
            }
            else if (IsValidToken(token))
            {
                tokenCounts[token] = tokenCounts.GetValueOrDefault(token) + 1;
            }
        }

        return tokenCounts
            .Select(kvp => new TokenInfo(kvp.Key, GetKeywordBonus(kvp.Key), kvp.Value))
            .ToList();
    }

    private static double GetKeywordBonus(string word) =>
        FinancialKeywords.GetFinancialKeywords().GetValueOrDefault(word, 1.0);

    private static IEnumerable<string> GenerateCjkNgrams(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        for (int n = Constants.CjkMinGram; n <= Constants.CjkMaxGram; n++)
        {
            if (text.Length < n) continue;
            for (int i = 0; i <= text.Length - n; i++)
            {
                yield return text.Substring(i, n);
            }
        }
    }

    private static bool ContainsCjk(string text) => text.Any(IsCjk);

    private static bool IsCjk(char ch) =>
        ch >= '\u4E00' && ch <= '\u9FFF' ||
        ch >= '\u3400' && ch <= '\u4DBF' ||
        ch >= '\uF900' && ch <= '\uFAFF';

    private static bool IsValidToken(string token) =>
        token.Length > 1 &&
        !StopWords.GetStopWords().Contains(token) &&
        token.All(c => c <= 0x7F && char.IsLetterOrDigit(c));

    #endregion

    #region 时间相关处理

    /// <summary>
    /// 从URL中提取日期并计算时效性得分
    /// </summary>
    private static double ExtractDateFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return 0.0;

        var patterns = new[]
        {
            @"/(\d{4})/(\d{1,2})/(\d{1,2})",
            @"/(\d{4})-(\d{1,2})-(\d{1,2})",
            @"/(\d{4})(\d{2})(\d{2})",
            @"(\d{4})年(\d{1,2})月(\d{1,2})日"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(url, pattern);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var year))
            {
                var ageYears = DateTime.Now.Year - year;
                return ageYears switch
                {
                    0 => 1.0,
                    1 => 0.9,
                    2 => 0.7,
                    3 => 0.5,
                    <= 5 => 0.3,
                    _ => 0.1
                };
            }
        }

        return 0.0;
    }

    /// <summary>
    /// 从内容中提取时间关键词并评分
    /// </summary>
    private static double ExtractDateFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var lowerContent = content.ToLowerInvariant();
        foreach (var (keyword, score) in TimeKeywords.GetTimeKeywords())
        {
            if (lowerContent.Contains(keyword))
            {
                return score;
            }
        }

        return 0.0;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 获取搜索结果的完整文本
    /// </summary>
    private static string GetFullText(TextSearchResult item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Name)) parts.Add(item.Name);
        if (!string.IsNullOrWhiteSpace(item.Value)) parts.Add(item.Value);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// 记录排序结果
    /// </summary>
    private void LogTopResults(List<ScoredResult> scoredResults)
    {
        _logger.LogDebug("启发式重排序完成，前3个结果得分情况:");
        var topResults = scoredResults.OrderByDescending(r => r.TotalScore).Take(3);

        int index = 1;
        foreach (var scored in topResults)
        {
            var snippet = GetFullText(scored.Item);
            if (snippet.Length > 50) snippet = snippet[..50] + "...";

            _logger.LogDebug("  {Index}. 总分: {Score:F2} (相关: {Rel:F2}, 时效: {Fresh:F2}, 长度: {Len:F2}) - {Title}",
                index++, scored.TotalScore, scored.RelevanceScore, scored.FreshnessScore, scored.LengthScore, snippet);
        }
    }

    #endregion
}

#region 静态配置类

/// <summary>
/// 停用词配置
/// </summary>
public static class StopWords
{
    private static readonly HashSet<string> _stopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // 中文停用词
        "的", "了", "在", "是", "我", "有", "和", "就", "不", "人", "都", "一", "一个", "上", "也", "很", "到", "说", "要", "去", "你", "会", "着", "没有", "看", "好", "自己", "这",
        // 英文停用词
        "a", "an", "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "from", "up", "about", "into", "through", "during", "before", "after", "above", "below", "between"
    };

    public static HashSet<string> GetStopWords() => _stopWords;
}

/// <summary>
/// 金融关键词配置
/// </summary>
public static class FinancialKeywords
{
    private static readonly Dictionary<string, double> _financialKeywords = new()
    {
        // 核心金融术语 - 最高权重
        ["股票"] = 2.0,
        ["债券"] = 2.0,
        ["基金"] = 2.0,
        ["期货"] = 2.0,
        ["期权"] = 2.0,
        ["外汇"] = 2.0,
        ["股价"] = 2.0,
        ["市值"] = 2.0,
        ["涨跌"] = 2.0,
        ["收益"] = 2.0,
        ["风险"] = 2.0,
        ["投资"] = 2.0,
        ["融资"] = 2.0,
        ["上市"] = 2.0,
        ["ipo"] = 2.0,
        ["并购"] = 2.0,
        ["重组"] = 2.0,

        // 重要指标 - 高权重
        ["pe"] = 1.8,
        ["pb"] = 1.8,
        ["roe"] = 1.8,
        ["roa"] = 1.8,
        ["eps"] = 1.8,
        ["净利润"] = 1.8,
        ["营收"] = 1.8,
        ["毛利率"] = 1.8,
        ["负债率"] = 1.8,
        ["市盈率"] = 1.8,
        ["市净率"] = 1.8,
        ["现金流"] = 1.8,
        ["分红"] = 1.8,
        ["股息"] = 1.8,
        ["估值"] = 1.8,

        // 市场术语 - 中等权重
        ["牛市"] = 1.5,
        ["熊市"] = 1.5,
        ["涨停"] = 1.5,
        ["跌停"] = 1.5,
        ["成交量"] = 1.5,
        ["换手率"] = 1.5,
        ["振幅"] = 1.5,
        ["均线"] = 1.5,
        ["支撑"] = 1.5,
        ["阻力"] = 1.5,
        ["突破"] = 1.5,
        ["回调"] = 1.5,
        ["反弹"] = 1.5,
        ["趋势"] = 1.5,

        // 行业板块 - 较高权重
        ["银行"] = 1.3,
        ["保险"] = 1.3,
        ["证券"] = 1.3,
        ["房地产"] = 1.3,
        ["科技"] = 1.3,
        ["医药"] = 1.3,
        ["消费"] = 1.3,
        ["制造"] = 1.3,
        ["新能源"] = 1.3,
        ["芯片"] = 1.3,
        ["5g"] = 1.3,
        ["人工智能"] = 1.3,
        ["区块链"] = 1.3
    };

    public static Dictionary<string, double> GetFinancialKeywords() => _financialKeywords;
}

/// <summary>
/// 时间关键词配置
/// </summary>
public static class TimeKeywords
{
    private static readonly Dictionary<string, double> _timeKeywords = new()
    {
        // 最新时间 - 最高时效性
        ["今日"] = 1.0,
        ["今天"] = 1.0,
        ["本周"] = 1.0,
        ["本月"] = 1.0,
        ["最新"] = 1.0,
        ["刚刚"] = 1.0,
        ["实时"] = 1.0,
        // 近期时间 - 高时效性
        ["昨日"] = 0.8,
        ["昨天"] = 0.8,
        ["上周"] = 0.8,
        ["近期"] = 0.8,
        ["最近"] = 0.8,
        // 过期时间 - 低时效性
        ["去年"] = 0.3,
        ["前年"] = 0.3,
        ["历史"] = 0.3,
        ["过去"] = 0.3
    };

    public static Dictionary<string, double> GetTimeKeywords() => _timeKeywords;
}

#endregion
