using MarketAssistant.Agents.StockSelection.Models;
using MarketAssistant.Applications.StockSelection.Models;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.StockScreener.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.StockSelection.Executors;

/// <summary>
/// 步骤3: AI分析筛选结果的 Executor（基于 Executor<TInput, TOutput> 模式）
/// 对筛选出的股票进行深度分析并生成推荐报告
/// </summary>
public sealed class AnalyzeStocksExecutor : Executor<ScreeningResult, StockSelectionResult>
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<AnalyzeStocksExecutor> _logger;

    public AnalyzeStocksExecutor(
        IChatClientFactory chatClientFactory,
        ILogger<AnalyzeStocksExecutor> logger) : base("AnalyzeStocks")
    {
        if (chatClientFactory == null) throw new ArgumentNullException(nameof(chatClientFactory));
        _chatClient = chatClientFactory.CreateClient();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async ValueTask<StockSelectionResult> HandleAsync(
        ScreeningResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤3/3] 对筛选结果进行AI分析");

        try
        {
            // 从输入中获取原始请求
            var originalRequest = input.OriginalRequest;
            if (originalRequest == null)
            {
                _logger.LogError("[步骤3/3] 缺少原始请求信息");
                return new StockSelectionResult
                {
                    Recommendations = new List<StockRecommendation>(),
                    ConfidenceScore = 0,
                    AnalysisSummary = "分析失败：缺少原始请求信息"
                };
            }

            // 检查是否有筛选结果
            if (input.ScreenedStocks.Count == 0)
            {
                _logger.LogWarning("[步骤3/3] 未筛选到符合条件的股票");
                return new StockSelectionResult
                {
                    Recommendations = new List<StockRecommendation>(),
                    ConfidenceScore = 0,
                    AnalysisSummary = "未找到符合条件的股票，建议放宽筛选条件。"
                };
            }

            // 格式化股票数据为文本
            var stocksDataText = FormatScreenedStocksForAnalysis(input.ScreenedStocks);

            // 构建分析提示词
            var systemPrompt = GetAnalysisInstructions(originalRequest.IsNewsAnalysis);
            var userPrompt = BuildAnalysisPrompt(originalRequest, stocksDataText);

            // 使用结构化输出
            var options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schema: AIJsonUtilities.CreateJsonSchema(typeof(StockSelectionResult)),
                    schemaName: "StockSelectionResult",
                    schemaDescription: "股票选择分析结果，包含推荐股票列表和分析报告"),
                Temperature = 0.2f,
                MaxOutputTokens = 8000
            };

            // 执行 AI 分析（纯分析，无工具调用）
            var response = await _chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt)
                ],
                options,
                cancellationToken);

            // 添加调试日志：输出AI原始响应
            _logger.LogDebug("[步骤3/3] AI原始响应: {Response}", response.Text);

            // 使用忽略大小写的选项进行反序列化
            var jsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            var result = JsonSerializer.Deserialize<StockSelectionResult>(response.Text, jsonOptions);

            if (result == null)
            {
                _logger.LogWarning("[步骤3/3] 响应反序列化失败，原始响应: {Response}", response.Text);
                result = CreateDefaultResult();
            }
            else if (result.Recommendations.Count == 0)
            {
                _logger.LogWarning("[步骤3/3] AI未生成任何推荐股票，原始响应: {Response}", response.Text);
            }

            _logger.LogInformation("[步骤3/3] 分析完成，推荐 {Count} 只股票，置信度: {Score}",
                result.Recommendations.Count, result.ConfidenceScore);

            // 返回最终结果（框架会自动传递）
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤3/3] AI分析失败");
            return new StockSelectionResult
            {
                Recommendations = new List<StockRecommendation>(),
                ConfidenceScore = 0,
                AnalysisSummary = $"分析失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 格式化筛选结果为分析数据（JSON格式）
    /// 只序列化非零值和非空值的属性，减少数据冗余
    /// </summary>
    private string FormatScreenedStocksForAnalysis(List<ScreenerStockInfo> stocks)
    {
        var simplifiedStocks = stocks.Select(s =>
        {
            var data = new Dictionary<string, object>();

            // 基本信息（必需）
            data["名称"] = s.Name;
            data["代码"] = s.Symbol;

            // 辅助方法：添加非零值
            void AddIfNotZero(string key, decimal value, int decimals = 2, decimal divisor = 1)
            {
                if (value != 0)
                {
                    var convertedValue = divisor != 1 ? value / divisor : value;
                    data[key] = Math.Round(convertedValue, decimals);
                }
            }

            // 价格与涨跌
            AddIfNotZero("当前价_元", s.Current);
            AddIfNotZero("涨跌幅_百分比", s.Pct);
            AddIfNotZero("当日振幅_百分比", s.ChgPct);

            // 市值
            AddIfNotZero("总市值_亿元", s.Mc, 2, 100000000);
            AddIfNotZero("流通市值_亿元", s.Fmc, 2, 100000000);

            // 成交数据
            AddIfNotZero("成交额_亿元", s.Amount, 2, 100000000);
            AddIfNotZero("成交量_万股", s.Volume);
            AddIfNotZero("量比", s.VolumeRatio);
            AddIfNotZero("换手率_百分比", s.Tr);

            // 估值指标
            AddIfNotZero("市盈率TTM", s.PeTtm);
            AddIfNotZero("市盈率LYR", s.PeLyr);
            AddIfNotZero("市净率", s.Pb);
            AddIfNotZero("市销率", s.Psr);

            // 每股指标
            AddIfNotZero("每股净资产_元", s.Bps);
            AddIfNotZero("每股收益_元", s.Eps);
            AddIfNotZero("股息收益率_百分比", s.DyL);

            // 盈利能力
            AddIfNotZero("净资产收益率ROE_百分比", s.RoeDiluted);
            AddIfNotZero("总资产报酬率_百分比", s.Niota);
            AddIfNotZero("净利润_亿元", s.NetProfit, 2, 100000000);
            AddIfNotZero("营业收入_亿元", s.TotalRevenue, 2, 100000000);

            // 增长指标
            AddIfNotZero("净利润同比增长_百分比", s.Npay);
            AddIfNotZero("营收同比增长_百分比", s.Oiy);

            // 历史涨跌
            AddIfNotZero("近5日涨跌幅_百分比", s.Pct5);
            AddIfNotZero("近10日涨跌幅_百分比", s.Pct10);
            AddIfNotZero("近20日涨跌幅_百分比", s.Pct20);
            AddIfNotZero("近60日涨跌幅_百分比", s.Pct60);
            AddIfNotZero("近120日涨跌幅_百分比", s.Pct120);
            AddIfNotZero("近250日涨跌幅_百分比", s.Pct250);
            AddIfNotZero("年初至今涨跌幅_百分比", s.PctCurrentYear);

            // 雪球社交热度
            AddIfNotZero("累计关注人数", s.Follow, 0);
            AddIfNotZero("累计讨论次数", s.Tweet, 0);
            AddIfNotZero("累计交易分享数", s.Deal, 0);
            AddIfNotZero("一周新增关注", s.Follow7d, 0);
            AddIfNotZero("一周新增讨论数", s.Tweet7d, 0);
            AddIfNotZero("一周新增交易分享数", s.Deal7d, 0);
            AddIfNotZero("一周关注增长率_百分比", s.Follow7dPct);
            AddIfNotZero("一周讨论增长率_百分比", s.Tweet7dPct);
            AddIfNotZero("一周交易分享增长率_百分比", s.Deal7dPct);

            return data;
        }).ToList();

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return JsonSerializer.Serialize(simplifiedStocks, jsonOptions);
    }

    /// <summary>
    /// 构建分析提示词
    /// </summary>
    private string BuildAnalysisPrompt(StockSelectionWorkflowRequest request, string stocksData)
    {
        var sb = new StringBuilder();

        if (request.IsNewsAnalysis)
        {
            sb.AppendLine("## 新闻内容");
            sb.AppendLine(request.Content);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## 用户需求");
            sb.AppendLine($"- 需求描述: {request.Content}");
            sb.AppendLine($"- 风险偏好: {request.RiskPreference}");

            if (request.InvestmentAmount.HasValue)
                sb.AppendLine($"- 投资金额: {request.InvestmentAmount:C}");

            if (request.InvestmentHorizon.HasValue)
                sb.AppendLine($"- 投资期限: {request.InvestmentHorizon}天");

            if (request.PreferredSectors.Any())
                sb.AppendLine($"- 偏好行业: {string.Join(", ", request.PreferredSectors)}");

            if (request.ExcludedSectors.Any())
                sb.AppendLine($"- 排除行业: {string.Join(", ", request.ExcludedSectors)}");

            sb.AppendLine();
        }

        sb.AppendLine("## 筛选出的股票数据（JSON格式）");
        sb.AppendLine(stocksData);
        sb.AppendLine();
        sb.AppendLine("## 分析任务");
        sb.AppendLine("请基于以上股票数据和用户需求，进行综合分析并生成推荐报告。");
        sb.AppendLine("- 从中选择最优的3-8只股票进行推荐");
        sb.AppendLine("- 说明推荐理由和风险提示");

        return sb.ToString();
    }

    /// <summary>
    /// 获取分析指令（System Prompt）
    /// </summary>
    private string GetAnalysisInstructions(bool isNewsAnalysis)
    {
        if (isNewsAnalysis)
        {
            return @"
你是一位专业的新闻热点分析师和投资顾问，擅长基于新闻热点提供股票投资建议。

## 分析流程
1. **理解新闻热点**：分析新闻内容，识别关键主题、受益行业和影响时长
2. **评估股票关联性**：判断每只股票与新闻热点的相关度
3. **综合评分**：结合财务数据（PE、PB、ROE等）、市场表现和新闻相关性进行评分
4. **筛选推荐**：选择3-8只最优股票，说明推荐理由

## 评估标准
- **新闻相关性**（权重40%）：股票所属行业、业务与新闻主题的关联程度
- **财务质量**（权重30%）：ROE、利润增长、估值水平
- **市场表现**（权重20%）：近期涨跌幅、成交活跃度、换手率
- **风险因素**（权重10%）：估值风险、行业风险、市场风险

## 输出格式
**严格按照以下JSON格式输出，不要添加markdown代码块标识：**

{
  ""analysisSummary"": ""新闻分析总结"",
  ""hotspotAnalysis"": ""热点分析和趋势判断"",
  ""marketImpact"": ""对市场的影响分析"",
  ""recommendations"": [
    {
      ""symbol"": ""股票代码"",
      ""name"": ""股票名称"",
      ""recommendationScore"": 85,
      ""reason"": ""推荐理由，必须说明与新闻的关联"",
      ""expectedReturn"": 15.5,
      ""riskLevel"": ""中风险"",
      ""newsRelevance"": ""与新闻的具体关联说明""
    }
  ],
  ""riskWarnings"": [""风险提示1"", ""风险提示2""],
  ""investmentStrategy"": ""投资策略建议"",
  ""confidenceScore"": 75
}

## 关键要求
⚠️ 推荐股票数量：3-8只（优选高质量标的）
⚠️ 数值类型：评分、收益率使用数字，不加引号
⚠️ 风险等级：只能是 ""低风险""、""中风险""、""高风险"" 之一
⚠️ 推荐理由：必须说明与新闻的具体关联，不能泛泛而谈
⚠️ 如果所有股票都不适合，recommendations 可以为空数组
";
        }
        else
        {
            return @"
你是一位专业的投资顾问，擅长根据用户的需求提供投资建议。

## 分析流程
1. **理解用户需求**：分析用户的投资目标、风险偏好、期限和金额
2. **评估股票质量**：审查财务指标（PE、PB、ROE、增长率等）
3. **匹配需求**：判断每只股票是否符合用户的风险偏好和投资目标
4. **筛选推荐**：选择3-8只最优股票，提供具体理由

## 评估标准
- **财务质量**（权重35%）：ROE、净利润增长、营收增长、现金流
- **估值合理性**（权重25%）：PE、PB是否合理，是否被低估
- **风险匹配度**（权重25%）：是否符合用户风险偏好
- **市场表现**（权重15%）：近期表现、流动性、市值规模

## 风险偏好匹配原则
- **保守型**：选择低PE、低PB、高ROE、稳定盈利、大盘股
- **稳健型**：选择中等估值、稳定增长、中大盘股
- **激进型**：选择高成长、创新业务、可接受较高估值

## 输出格式
**严格按照以下JSON格式输出，不要添加markdown代码块标识：**

{
  ""analysisSummary"": ""分析总结"",
  ""marketEnvironmentAnalysis"": ""市场环境分析"",
  ""recommendations"": [
    {
      ""symbol"": ""股票代码"",
      ""name"": ""股票名称"",
      ""recommendationScore"": 85,
      ""reason"": ""推荐理由，说明为何符合用户需求"",
      ""expectedReturn"": 15.5,
      ""riskLevel"": ""中风险""
    }
  ],
  ""riskWarnings"": [""风险提示1"", ""风险提示2""],
  ""investmentAdvice"": ""投资建议"",
  ""confidenceScore"": 75
}

## 关键要求
⚠️ 推荐股票数量：3-8只（优选高质量标的）
⚠️ 数值类型：评分、收益率使用数字，不加引号
⚠️ 风险等级：只能是 ""低风险""、""中风险""、""高风险"" 之一
⚠️ 推荐理由：必须结合具体财务数据和用户需求
⚠️ 如果所有股票都不适合，recommendations 可以为空数组
";
        }
    }

    private StockSelectionResult CreateDefaultResult()
    {
        return new StockSelectionResult
        {
            Recommendations = new List<StockRecommendation>(),
            ConfidenceScore = 0,
            AnalysisSummary = "解析分析结果失败"
        };
    }
}
