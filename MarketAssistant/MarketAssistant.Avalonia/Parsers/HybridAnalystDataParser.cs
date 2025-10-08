using MarketAssistant.Avalonia.Views.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Parsers;

/// <summary>
/// 混合分析师数据解析器 - 结合正则表达式速度和AI准确性
/// </summary>
public class HybridAnalystDataParser : IAnalystDataParser
{
    private readonly RegexAnalystDataParser _regexParser;
    private readonly AIAnalystDataParser _aiParser;
    private readonly ILogger<HybridAnalystDataParser>? _logger;

    /// <summary>
    /// 初始化混合解析器
    /// </summary>
    /// <param name="kernel">语义内核实例</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="enableAIFallback">是否启用AI回退机制</param>
    public HybridAnalystDataParser(Kernel kernel, ILogger<HybridAnalystDataParser>? logger = null)
    {
        _regexParser = new RegexAnalystDataParser();
        _aiParser = new AIAnalystDataParser(kernel);
        _logger = logger;
    }

    /// <summary>
    /// 异步解析分析师返回的文本内容
    /// </summary>
    /// <param name="content">分析师返回的文本内容</param>
    /// <returns>解析后的结构化数据</returns>
    public async Task<AnalystResult> ParseDataAsync(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return new AnalystResult();
        }

        try
        {
            //先使用AI解析，AI解析报错在使用正则解析作为兜底
            //完整性验证和结果合并逻辑暂时不用
            var aiResult = await _aiParser.ParseDataAsync(content);
            if (aiResult.OverallScore > 0)
            {
                return aiResult;
            }
            _logger?.LogWarning("AI解析结果不完整，使用正则解析作为兜底");
            return await _regexParser.ParseDataAsync(content);
        }
        catch (Exception ex)
        {
            // 如果所有解析器都失败，返回基础结果
            _logger?.LogError(ex, "所有解析器都失败");
            return new AnalystResult
            {
                StockSymbol = "解析失败",
                OverallScore = 0,
                ConfidencePercentage = 0,
                InvestmentRating = "无法评级",
                RiskLevel = "未知"
            };
        }
    }

    /// <summary>
    /// 评估解析结果的完整性
    /// </summary>
    /// <param name="result">解析结果</param>
    /// <returns>完整性评分 (0-1)</returns>
    private float EvaluateCompleteness(AnalystResult result)
    {
        var score = 0f;
        var totalFields = 10f; // 总字段数

        // 核心字段权重更高
        if (!string.IsNullOrEmpty(result.StockSymbol)) score += 1.5f;
        if (result.OverallScore > 0) score += 1.5f;
        if (!string.IsNullOrEmpty(result.InvestmentRating)) score += 1.5f;
        if (!string.IsNullOrEmpty(result.TargetPrice)) score += 1.0f;
        if (!string.IsNullOrEmpty(result.RiskLevel)) score += 1.0f;
        if (result.DimensionScores.Count > 0) score += 1.0f;
        if (!string.IsNullOrEmpty(result.ConsensusInfo)) score += 0.5f;
        if (result.InvestmentHighlights.Count > 0) score += 0.5f;
        if (result.RiskFactors.Count > 0) score += 0.5f;
        if (result.AnalysisData.Count > 0) score += 1.0f;

        return Math.Min(score / totalFields, 1.0f);
    }

    /// <summary>
    /// 合并AI和正则解析结果，优先使用AI结果
    /// </summary>
    /// <param name="aiResult">AI解析结果</param>
    /// <param name="regexResult">正则解析结果</param>
    /// <returns>合并后的结果</returns>
    private AnalystResult MergeResults(AnalystResult aiResult, AnalystResult regexResult)
    {
        // 以AI结果为基础，正则结果作为补充
        var merged = new AnalystResult
        {
            StockSymbol = !string.IsNullOrEmpty(aiResult.StockSymbol) ? aiResult.StockSymbol : regexResult.StockSymbol,
            OverallScore = aiResult.OverallScore > 0 ? aiResult.OverallScore : regexResult.OverallScore,
            ConfidencePercentage = aiResult.ConfidencePercentage > 0 ? aiResult.ConfidencePercentage : regexResult.ConfidencePercentage,
            InvestmentRating = !string.IsNullOrEmpty(aiResult.InvestmentRating) ? aiResult.InvestmentRating : regexResult.InvestmentRating,
            TargetPrice = !string.IsNullOrEmpty(aiResult.TargetPrice) ? aiResult.TargetPrice : regexResult.TargetPrice,
            RiskLevel = !string.IsNullOrEmpty(aiResult.RiskLevel) ? aiResult.RiskLevel : regexResult.RiskLevel,

            // 文本字段优先使用AI结果
            ConsensusInfo = !string.IsNullOrEmpty(aiResult.ConsensusInfo) ? aiResult.ConsensusInfo : regexResult.ConsensusInfo,
            DisagreementInfo = !string.IsNullOrEmpty(aiResult.DisagreementInfo) ? aiResult.DisagreementInfo : regexResult.DisagreementInfo,

            // 列表字段合并，AI结果优先
            InvestmentHighlights = MergeLists(aiResult.InvestmentHighlights, regexResult.InvestmentHighlights),
            RiskFactors = MergeLists(aiResult.RiskFactors, regexResult.RiskFactors),
            OperationSuggestions = MergeLists(aiResult.OperationSuggestions, regexResult.OperationSuggestions),
            AnalysisData = MergeAnalysisData(aiResult.AnalysisData, regexResult.AnalysisData)
        };

        // 合并维度评分
        merged.DimensionScores = new Dictionary<string, float>(aiResult.DimensionScores);
        foreach (var kvp in regexResult.DimensionScores)
        {
            if (!merged.DimensionScores.ContainsKey(kvp.Key))
            {
                merged.DimensionScores[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }

    /// <summary>
    /// 合并两个字符串列表，AI结果优先，去重并保持顺序
    /// </summary>
    private List<string> MergeLists(List<string> aiList, List<string> regexList)
    {
        var merged = new List<string>();

        // 优先添加AI列表的项目
        if (aiList != null)
        {
            merged.AddRange(aiList.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        // 再添加正则列表中不重复的项目
        if (regexList != null)
        {
            foreach (var item in regexList.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!merged.Any(existing => existing.Trim().Equals(item.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(item);
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// 合并分析数据项，AI结果优先
    /// </summary>
    private List<AnalysisDataItem> MergeAnalysisData(List<AnalysisDataItem> aiData, List<AnalysisDataItem> regexData)
    {
        var merged = new List<AnalysisDataItem>();

        // 优先添加AI数据
        if (aiData != null)
        {
            merged.AddRange(aiData);
        }

        // 添加正则数据中不重复的项目
        if (regexData != null)
        {
            foreach (var item in regexData)
            {
                // 检查是否已存在相同名称和类型的数据项
                var existing = merged.FirstOrDefault(x =>
                    x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) &&
                    x.DataType.Equals(item.DataType, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    merged.Add(item);
                }
                // 如果存在重复项，保持AI结果（不添加正则结果）
            }
        }

        return merged;
    }
}