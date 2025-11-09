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

            var result = JsonSerializer.Deserialize<StockSelectionResult>(response.Text, JsonSerializerOptions.Web);

            if (result == null)
            {
                _logger.LogWarning("[步骤3/3] 响应反序列化失败");
                result = CreateDefaultResult();
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
    /// 格式化筛选结果为分析文本
    /// </summary>
    private string FormatScreenedStocksForAnalysis(List<ScreenerStockInfo> stocks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"【筛选结果数据】");
        sb.AppendLine($"共筛选出 {stocks.Count} 只股票：");
        sb.AppendLine();

        foreach (var stock in stocks)
        {
            sb.AppendLine($"股票名称: {stock.Name}");
            sb.AppendLine($"股票代码: {stock.Symbol}");
            sb.AppendLine($"当前价: {stock.Current:F2} 元");
            sb.AppendLine($"涨跌幅: {stock.Pct:F2}%");
            sb.AppendLine($"市盈率TTM: {stock.PeTtm:F2}");
            sb.AppendLine($"市净率: {stock.Pb:F2}");
            sb.AppendLine($"ROE: {stock.RoeDiluted:F2}%");
            sb.AppendLine($"总市值: {stock.Mc / 100000000:F2} 亿");
            sb.AppendLine($"流通市值: {stock.Fmc / 100000000:F2} 亿");
            sb.AppendLine($"成交额: {stock.Amount / 100000000:F2} 亿");
            sb.AppendLine($"换手率: {stock.Tr:F2}%");
            sb.AppendLine($"净利润: {stock.NetProfit / 100000000:F2} 亿");
            sb.AppendLine($"营业收入: {stock.TotalRevenue / 100000000:F2} 亿");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建分析提示词
    /// </summary>
    private string BuildAnalysisPrompt(StockSelectionWorkflowRequest request, string stocksData)
    {
        var sb = new StringBuilder();

        if (request.IsNewsAnalysis)
        {
            sb.AppendLine("【新闻内容】");
            sb.AppendLine(request.Content);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("【用户需求】");
            sb.AppendLine($"需求描述: {request.Content}");
            sb.AppendLine($"风险偏好: {request.RiskPreference}");

            if (request.InvestmentAmount.HasValue)
                sb.AppendLine($"投资金额: {request.InvestmentAmount:C}");

            if (request.InvestmentHorizon.HasValue)
                sb.AppendLine($"投资期限: {request.InvestmentHorizon}天");

            if (request.PreferredSectors.Any())
                sb.AppendLine($"偏好行业: {string.Join(", ", request.PreferredSectors)}");

            if (request.ExcludedSectors.Any())
                sb.AppendLine($"排除行业: {string.Join(", ", request.ExcludedSectors)}");

            sb.AppendLine();
        }

        sb.AppendLine(stocksData);
        sb.AppendLine("【任务】");
        sb.AppendLine("请基于以上筛选结果，分析每只股票是否符合需求，并生成推荐报告（JSON格式）。");
        sb.AppendLine("注意：股票数据已提供，你只需分析，不需要调用工具。");

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

## 核心任务
1. 分析筛选出的股票结果，结合新闻热点内容
2. 识别与新闻相关的投资机会和受益股票
3. 评估新闻热点的持续性和市场影响
4. 提供基于新闻驱动的投资建议

## 输出格式
请仅返回纯JSON数据，不要包含任何markdown代码块标识（如```json或```）。

### 必需字段
- analysisSummary: 字符串，新闻分析总结
- hotspotAnalysis: 字符串，热点分析和趋势判断
- marketImpact: 字符串，对市场的影响分析
- recommendations: 数组，推荐股票列表
- riskWarnings: 字符串数组，风险提示
- investmentStrategy: 字符串，投资策略建议
- confidenceScore: 数值(0-100)，整体置信度评分

### recommendations 数组元素
- symbol: 字符串，股票代码
- name: 字符串，股票名称
- recommendationScore: 数值(0-100)，推荐评分
- reason: 字符串，推荐理由（必须包含与新闻热点的关联性）
- expectedReturn: 数值(如: 15.5)，预期收益率
- riskLevel: 字符串，只能是 ""低风险""、""中风险""、""高风险"" 之一
- newsRelevance: 字符串，与新闻的相关性说明

## 关键注意事项
⚠️ expectedReturn 必须是数值类型
⚠️ 所有评分字段使用数值，不加引号
⚠️ 必须说明每只股票与新闻热点的具体关联
⚠️ 推荐股票数量控制在3-8只
⚠️ 确保JSON格式正确，避免多余的逗号或语法错误
";
        }
        else
        {
            return @"
你是一位专业的投资顾问，擅长根据用户的需求提供投资建议。

## 核心任务
1. 分析筛选出的股票结果
2. 根据用户风险偏好选择合适的推荐股票
3. 基于用户需求提供投资建议和推荐理由

## 输出格式
请仅返回纯JSON数据，不要包含任何markdown代码块标识（如```json或```）。

### 必需字段
- analysisSummary: 字符串，分析总结
- marketEnvironmentAnalysis: 字符串，市场环境分析
- recommendations: 数组，推荐股票列表
- riskWarnings: 字符串数组，风险提示
- investmentAdvice: 字符串，投资建议
- confidenceScore: 数值(0-100)，置信度评分

### recommendations 数组元素
- symbol: 字符串，股票代码
- name: 字符串，股票名称
- recommendationScore: 数值(0-100)，推荐评分
- reason: 字符串，推荐理由
- expectedReturn: 数值(如: 15.5)，预期收益率
- riskLevel: 字符串，只能是 ""低风险""、""中风险""、""高风险"" 之一

## 关键注意事项
⚠️ expectedReturn 必须是数值类型
⚠️ 所有评分字段使用数值，不加引号
⚠️ 确保JSON格式正确，避免多余的逗号或语法错误
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
