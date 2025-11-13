using MarketAssistant.Agents.StockSelection.Models;
using MarketAssistant.Applications.StockSelection.Models;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.StockScreener.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

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
    /// 获取分析指令（System Prompt）- 统一的分析指令
    /// </summary>
    private string GetAnalysisInstructions(bool isNewsAnalysis)
    {
        return @"
你是专业的投资顾问，基于用户需求/新闻热点和股票数据提供投资建议。

## 核心职责
从第三方推荐的股票中，进行多维度分析，输出结构化推荐报告（3-8只股票）。

## 评估维度（灵活权重）
- 财务质量：ROE、利润、成长性、现金流、EPS/BPS
- 估值水平：PE/PB/PS合理性、低估/高估判断、股息率
- 市场表现：涨跌幅、流动性（成交额/换手率）、市场情绪
- 需求匹配：风险偏好、期限、行业偏好，或新闻关联度
- 社交热度：雪球关注/讨论及增长（参考）

## 输出要求
严格按JSON Schema输出，关键约束：
1. **数值类型**：评分、收益率、仓位、价格等用数字（不加引号）
2. **风险等级**：仅限 ""低风险""/""中风险""/""高风险""
3. **推荐理由**：必须包含具体财务数据+估值判断+需求匹配说明
4. **可选字段**：无法预测的字段设null
5. **空结果**：无合适股票时，recommendations设空数组[]
";
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
