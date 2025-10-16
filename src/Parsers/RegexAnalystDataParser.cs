using MarketAssistant.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MarketAssistant.Parsers;

/// <summary>
/// 基于正则表达式的快速分析师数据解析器 - 高性能替代AI解析器
/// </summary>
public class RegexAnalystDataParser : IAnalystDataParser
{
    private static readonly Dictionary<string, Regex> _compiledRegexes = new()
    {
        // 股票代码匹配 - 增强匹配能力
        ["StockSymbol"] = new Regex(@"(?:股票代码|股票)[：:]\s*(\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 当前价格匹配 - 新增
        ["CurrentPrice"] = new Regex(@"当前价格[：:]\s*([0-9]+\.?[0-9]*)\s*元?", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 综合评分匹配 - 增强格式支持
        ["OverallScore"] = new Regex(@"综合评分[：:]?\s*([0-9]+\.?[0-9]*)\s*[分]?", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 置信度匹配 - 增强格式支持
        ["Confidence"] = new Regex(@"置信度[：:]?\s*([0-9]+\.?[0-9]*)\s*%?", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 投资评级匹配 - 扩展评级类型
        ["Rating"] = new Regex(@"(?:综合评级|投资评级|评级)[：:]?\s*(强烈买入|买入|持有|减持|卖出|强烈卖出|中性)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 目标价格匹配 - 增强格式支持
        ["TargetPrice"] = new Regex(@"(?:目标价格|目标区间|目标价)[：:]?\s*([0-9]+\.?[0-9]*\s*[-~至到]\s*[0-9]+\.?[0-9]*\s*元?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 价格变化预期匹配 - 新增
        ["PriceChange"] = new Regex(@"(?:上涨空间|下跌风险)[：:]?\s*([+\-]?[0-9]+\.?[0-9]*\s*%?)\s*/\s*(?:下跌风险|上涨空间)[：:]?\s*([+\-]?[0-9]+\.?[0-9]*\s*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 风险等级匹配 - 保持原有
        ["RiskLevel"] = new Regex(@"(?:风险水平|风险等级)[：:]?\s*(低风险|中风险|高风险|低|中|高)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 维度评分匹配 - 增强匹配能力
        ["DimensionScores"] = new Regex(@"([^：:\n\r]+?)评估[：:]?\s*([0-9]+\.?[0-9]*)\s*[分]?", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // 共识信息匹配 - 增强多行匹配
        ["Consensus"] = new Regex(@"(?:核心共识|分析师共识)[：:]?\s*([^\n\r]*?)(?=\s*\[|主要分歧|最终投资|核心投资|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),

        // 分歧信息匹配 - 增强匹配
        ["Disagreement"] = new Regex(@"(?:主要分歧|分歧)[：:]?\s*([^\n\r]*?)(?=短期|最终投资|核心投资|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),

        // 投资亮点匹配 - 增强匹配
        ["Highlights"] = new Regex(@"(?:投资亮点|核心投资逻辑)[：:]?\s*([^\n\r]*?)(?=关键风险|风险因素|操作建议|关键指标|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),

        // 风险因素匹配 - 增强匹配
        ["RiskFactors"] = new Regex(@"(?:关键风险|风险因素)[：:]?\s*([^\n\r]*?)(?=操作建议|关键指标|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),

        // 操作建议匹配 - 增强匹配
        ["Operations"] = new Regex(@"操作建议[：:]?\s*([^\n\r]*?)(?=关键指标|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline)
    };

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

        return await Task.Run(() => ParseData(content));
    }

    /// <summary>
    /// 同步解析数据 - 核心解析逻辑
    /// </summary>
    /// <param name="content">分析师返回的文本内容</param>
    /// <returns>解析后的结构化数据</returns>
    private AnalystResult ParseData(string content)
    {
        var result = new AnalystResult();

        try
        {
            // 解析股票代码
            result.StockSymbol = ExtractSingleValue(content, "StockSymbol") ?? string.Empty;

            // 解析综合评分
            if (float.TryParse(ExtractSingleValue(content, "OverallScore"), NumberStyles.Float, CultureInfo.InvariantCulture, out var overallScore))
            {
                result.OverallScore = overallScore;
            }

            // 解析置信度
            if (float.TryParse(ExtractSingleValue(content, "Confidence"), NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
            {
                result.ConfidencePercentage = confidence;
            }

            // 解析投资评级
            result.InvestmentRating = ExtractSingleValue(content, "Rating") ?? string.Empty;

            // 解析目标价格
            result.TargetPrice = ExtractSingleValue(content, "TargetPrice") ?? string.Empty;

            // 解析价格变化预期
            result.PriceChange = ExtractPriceChangeExpectation(content);

            // 解析风险等级
            result.RiskLevel = ExtractSingleValue(content, "RiskLevel") ?? string.Empty;

            // 解析维度评分
            result.DimensionScores = ExtractDimensionScores(content);

            // 解析共识和分歧信息
            result.ConsensusInfo = ExtractSingleValue(content, "Consensus")?.Trim() ?? string.Empty;
            result.DisagreementInfo = ExtractSingleValue(content, "Disagreement")?.Trim() ?? string.Empty;

            // 解析投资亮点
            var highlights = ExtractSingleValue(content, "Highlights");
            if (!string.IsNullOrEmpty(highlights))
            {
                result.InvestmentHighlights = SplitAndClean(highlights);
            }

            // 解析风险因素
            var riskFactors = ExtractSingleValue(content, "RiskFactors");
            if (!string.IsNullOrEmpty(riskFactors))
            {
                result.RiskFactors = SplitAndClean(riskFactors);
            }

            // 解析操作建议
            var operations = ExtractSingleValue(content, "Operations");
            if (!string.IsNullOrEmpty(operations))
            {
                result.OperationSuggestions = SplitAndClean(operations);
            }

            // 解析分析数据项
            result.AnalysisData = ExtractAnalysisData(content);
        }
        catch (Exception ex)
        {
            // 解析失败时记录错误但不抛出异常
            Console.WriteLine($"正则解析失败: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 提取单个值
    /// </summary>
    private string? ExtractSingleValue(string content, string regexKey)
    {
        if (!_compiledRegexes.TryGetValue(regexKey, out var regex))
            return null;

        var match = regex.Match(content);
        return match.Success ? match.Groups[match.Groups.Count - 1].Value.Trim() : null;
    }

    /// <summary>
    /// 提取维度评分
    /// </summary>
    private Dictionary<string, float> ExtractDimensionScores(string content)
    {
        var scores = new Dictionary<string, float>();
        var regex = _compiledRegexes["DimensionScores"];
        var matches = regex.Matches(content);

        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 3)
            {
                var dimension = match.Groups[1].Value.Trim();
                var scoreText = match.Groups[2].Value.Trim();

                if (float.TryParse(scoreText, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                {
                    scores[dimension] = score;
                }
            }
        }

        return scores;
    }

    /// <summary>
    /// 分割和清理文本
    /// </summary>
    private List<string> SplitAndClean(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string>();

        // 按常见分隔符分割
        var separators = new[] { ';', '；', ',', '，', '\n', '\r' };
        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim())
                  .Where(s => !string.IsNullOrEmpty(s))
                  .ToList();
    }

    /// <summary>
    /// 提取分析数据项
    /// </summary>
    private List<AnalysisDataItem> ExtractAnalysisData(string content)
    {
        var analysisData = new List<AnalysisDataItem>();

        // 添加当前价格作为基础数据项
        var currentPrice = ExtractSingleValue(content, "CurrentPrice");
        if (!string.IsNullOrEmpty(currentPrice))
        {
            analysisData.Add(new AnalysisDataItem
            {
                DataType = "基础信息",
                Name = "当前价格",
                Value = currentPrice,
                Unit = "元",
                Signal = "中性",
                Impact = "基准",
                Strategy = "作为投资决策的基准价格"
            });
        }

        // 提取技术指标相关数据
        ExtractTechnicalIndicators(content, analysisData);

        // 提取基本面指标
        ExtractFundamentalIndicators(content, analysisData);

        // 提取财务数据
        ExtractFinancialData(content, analysisData);

        return analysisData;
    }

    /// <summary>
    /// 提取技术指标
    /// </summary>
    private void ExtractTechnicalIndicators(string content, List<AnalysisDataItem> analysisData)
    {
        // 匹配技术指标模式: "MACD金叉", "60日均线", "支撑位30.5-31.2"
        var technicalPatterns = new[]
        {
            new Regex(@"(MACD|KDJ|RSI|BOLL|均线|MA)([^，,。\n\r]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(支撑位|压力位|阻力位)[：:]?\s*([0-9]+\.?[0-9]*\s*[-~至到]?\s*[0-9]*\.?[0-9]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(成交量)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[手股份]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(换手率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(?:趋势|走势)[：:]?\s*(上涨|下跌|震荡|横盘|强势|弱势)", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        foreach (var pattern in technicalPatterns)
        {
            var matches = pattern.Matches(content);
            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 2)
                {
                    analysisData.Add(new AnalysisDataItem
                    {
                        DataType = "TechnicalIndicator",
                        Name = match.Groups[1].Value.Trim(),
                        Value = match.Groups.Count > 2 ? match.Groups[2].Value.Trim() : match.Groups[1].Value.Trim(),
                        Signal = DetermineSignal(match.Value)
                    });
                }
            }
        }
    }

    /// <summary>
    /// 提取基本面指标
    /// </summary>
    private void ExtractFundamentalIndicators(string content, List<AnalysisDataItem> analysisData)
    {
        // 基本面指标正则表达式模式 - 增强版
        var patterns = new List<Regex>
        {
            new Regex(@"(营收|收入|营业收入)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(净利润|利润)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(毛利率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(ROE|净资产收益率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(ROA|总资产收益率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(资产负债率|负债率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(市值)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(流通市值)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(每股收益|EPS)[：:]?\s*([0-9]+\.?[0-9]*[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(每股净资产|BVPS)[：:]?\s*([0-9]+\.?[0-9]*[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(营收增长率|收入增长)[：:]?\s*([+\-]?[0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(净利润增长率|利润增长)[：:]?\s*([+\-]?[0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(基本面评估)[：:]?\s*([0-9]+\.?[0-9]*)[分]?", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(行业地位|市场地位)[：:]?\s*(领先|优势|一般|落后)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(竞争优势)[：:]?\s*([^\n\r，,。]{1,50})", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(PE|PB|ROE|ROIC|毛利率|净利率|市占率)\s*[：:]?\s*([0-9]+\.?[0-9]*\s*[%倍x]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        foreach (var regex in patterns)
        {
            var matches = regex.Matches(content);
            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count >= 3)
                {
                    analysisData.Add(new AnalysisDataItem
                    {
                        DataType = "FundamentalIndicator",
                        Name = match.Groups[1].Value.Trim(),
                        Value = match.Groups[2].Value.Trim(),
                        Signal = DetermineSignal(match.Value),
                        Impact = DetermineImpact(match.Groups[1].Value, match.Value),
                        Strategy = $"基于{match.Groups[1].Value}的投资建议"
                    });
                }
            }
        }
    }

    /// <summary>
    /// 提取财务数据
    /// </summary>
    private void ExtractFinancialData(string content, List<AnalysisDataItem> analysisData)
    {
        // 财务数据正则表达式模式 - 增强版
        var patterns = new List<Regex>
        {
            new Regex(@"(现金流|经营现金流|自由现金流)[：:]?\s*([+\-]?[0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(负债率|资产负债率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(流动比率)[：:]?\s*([0-9]+\.?[0-9]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(速动比率)[：:]?\s*([0-9]+\.?[0-9]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(存货周转率)[：:]?\s*([0-9]+\.?[0-9]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(应收账款周转率)[：:]?\s*([0-9]+\.?[0-9]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(总资产周转率)[：:]?\s*([0-9]+\.?[0-9]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(权益乘数)[：:]?\s*([0-9]+\.?[0-9]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(股息率|分红率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(研发费用|研发投入)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(研发费用率|研发投入比)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(销售费用率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(管理费用率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(财务费用率)[：:]?\s*([0-9]+\.?[0-9]*%?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(净资产|股东权益)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(总资产)[：:]?\s*([0-9]+\.?[0-9]*[万亿千百十]?[元]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        foreach (var regex in patterns)
        {
            var matches = regex.Matches(content);
            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count >= 3)
                {
                    analysisData.Add(new AnalysisDataItem
                    {
                        DataType = "财务数据",
                        Name = match.Groups[1].Value,
                        Value = match.Groups[2].Value.Trim(),
                        Signal = DetermineSignal(match.Value),
                        Impact = DetermineImpact(match.Groups[1].Value, match.Value),
                        Strategy = $"基于{match.Groups[1].Value}的财务分析"
                    });
                }
            }
        }
    }

    /// <summary>
    /// 根据指标名称和值确定信号
    /// </summary>
    private string DetermineSignal(string text)
    {
        var lowerText = text.ToLower();

        // 趋势相关信号
        if (lowerText.Contains("金叉") || lowerText.Contains("突破") || lowerText.Contains("上涨") || lowerText.Contains("强势") || lowerText.Contains("买入"))
            return "看涨";
        if (lowerText.Contains("死叉") || lowerText.Contains("跌破") || lowerText.Contains("下跌") || lowerText.Contains("弱势") || lowerText.Contains("卖出"))
            return "看跌";
        if (lowerText.Contains("震荡") || lowerText.Contains("横盘") || lowerText.Contains("持有"))
            return "中性";

        // 评级信号判断
        if (lowerText.Contains("强烈买入") || lowerText.Contains("买入")) return "看涨";
        if (lowerText.Contains("强烈卖出") || lowerText.Contains("卖出")) return "看跌";
        if (lowerText.Contains("持有") || lowerText.Contains("中性")) return "中性";

        return "中性";
    }

    /// <summary>
    /// 根据指标名称和值确定影响程度
    /// </summary>
    private string DetermineImpact(string indicatorName, string value)
    {
        var lowerName = indicatorName.ToLower();
        var lowerValue = value.ToLower();

        // 强度关键词判断
        if (lowerValue.Contains("强烈") || lowerValue.Contains("显著") || lowerValue.Contains("大幅") || lowerValue.Contains("急剧"))
            return "高影响";
        if (lowerValue.Contains("轻微") || lowerValue.Contains("小幅") || lowerValue.Contains("微弱"))
            return "低影响";
        if (lowerValue.Contains("适度") || lowerValue.Contains("温和") || lowerValue.Contains("稳定"))
            return "中等影响";

        // 核心指标高影响
        if (lowerName.Contains("综合评分") || lowerName.Contains("投资评级") || lowerName.Contains("目标价格"))
            return "高影响";

        // 财务核心指标
        if (lowerName.Contains("营收") || lowerName.Contains("净利润") || lowerName.Contains("roe") || lowerName.Contains("现金流"))
            return "高影响";

        // 技术指标一般为中等影响
        if (lowerName.Contains("rsi") || lowerName.Contains("macd") || lowerName.Contains("kdj"))
            return "中等影响";

        // 数值范围判断
        if (value.Contains("%"))
        {
            var numericValue = value.Replace("%", "").Replace("+", "").Replace("-", "");
            if (double.TryParse(numericValue, out double percentage))
            {
                if (Math.Abs(percentage) > 50) return "高影响";
                if (Math.Abs(percentage) < 10) return "低影响";
                return "中等影响";
            }
        }

        return "中等影响";
    }

    /// <summary>
    /// 提取价格变化预期
    /// </summary>
    private string ExtractPriceChangeExpectation(string content)
    {
        var priceChangeMatch = _compiledRegexes["PriceChange"].Match(content);
        if (priceChangeMatch.Success && priceChangeMatch.Groups.Count >= 3)
        {
            var upside = priceChangeMatch.Groups[1].Value.Trim();
            var downside = priceChangeMatch.Groups[2].Value.Trim();
            return $"上涨空间: {upside}, 下跌风险: {downside}";
        }
        return string.Empty;
    }
}