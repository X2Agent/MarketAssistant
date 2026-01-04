using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Applications.AssetScreener.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection.Executors;

/// <summary>
/// 步骤3: AI分析股票筛选结果的 Executor
/// 对筛选出的股票进行深度分析并生成推荐报告
/// </summary>
public sealed class AnalyzeStocksExecutor : Executor<AssetScreeningResult, InvestmentSelectionResult>
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<AnalyzeStocksExecutor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public AnalyzeStocksExecutor(
        IChatClientFactory chatClientFactory,
        ILogger<AnalyzeStocksExecutor> logger) : base("AnalyzeStocks")
    {
        if (chatClientFactory == null) throw new ArgumentNullException(nameof(chatClientFactory));
        _chatClientFactory = chatClientFactory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async ValueTask<InvestmentSelectionResult> HandleAsync(
        AssetScreeningResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤3/3-股票] 对筛选结果进行AI分析");

        try
        {
            var originalRequest = input.OriginalRequest;
            if (originalRequest == null)
            {
                _logger.LogError("[步骤3/3-股票] 缺少原始请求信息");
                return new InvestmentSelectionResult
                {
                    Recommendations = new List<InvestmentRecommendation>(),
                    ConfidenceScore = 0,
                    AnalysisSummary = "分析失败：缺少原始请求信息"
                };
            }

            if (originalRequest.MarketType != MarketType.AShare)
            {
                throw new InvalidOperationException($"AnalyzeStocksExecutor 仅支持 AShare 市场，当前市场类型: {originalRequest.MarketType}");
            }

            if (input.ScreenedAssets.Count == 0)
            {
                _logger.LogWarning("[步骤3/3-股票] 未筛选到符合条件的股票");
                return new InvestmentSelectionResult
                {
                    Recommendations = new List<InvestmentRecommendation>(),
                    ConfidenceScore = 0,
                    AnalysisSummary = "未找到符合条件的股票，建议放宽筛选条件。"
                };
            }

            var stocksDataText = FormatScreenedStocksForAnalysis(input.ScreenedAssets);

            var systemPrompt = GetAnalysisInstructions(originalRequest.IsNewsAnalysis);
            var userPrompt = BuildAnalysisPrompt(originalRequest, stocksDataText);

            var options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schema: AIJsonUtilities.CreateJsonSchema(typeof(InvestmentSelectionResult)),
                    schemaName: "InvestmentSelectionResult",
                    schemaDescription: "投资选择分析结果，包含推荐股票列表和分析报告"),
                Temperature = 0.2f,
                MaxOutputTokens = 8000
            };

            var response = await _chatClientFactory.CreateClient().GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt)
                ],
                options,
                cancellationToken);

            _logger.LogDebug("[步骤3/3-股票] AI原始响应: {Response}", response.Text);

            var result = JsonSerializer.Deserialize<InvestmentSelectionResult>(response.Text, JsonOptions);

            if (result == null)
            {
                _logger.LogWarning("[步骤3/3-股票] 响应反序列化失败，原始响应: {Response}", response.Text);
                result = CreateDefaultResult();
            }
            else
            {
                var validationErrors = ValidateResult(result);
                if (validationErrors.Count > 0)
                {
                    _logger.LogWarning("[步骤3/3-股票] AI返回数据验证失败: {Errors}", string.Join("; ", validationErrors));
                }

                if (result.Recommendations.Count == 0)
                {
                    _logger.LogWarning("[步骤3/3-股票] AI未生成任何推荐股票，原始响应: {Response}", response.Text);
                }
            }

            _logger.LogInformation("[步骤3/3-股票] 分析完成，推荐 {Count} 只股票，置信度: {Score}",
                result.Recommendations.Count, result.ConfidenceScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤3/3-股票] AI分析失败");
            return new InvestmentSelectionResult
            {
                Recommendations = new List<InvestmentRecommendation>(),
                ConfidenceScore = 0,
                AnalysisSummary = $"分析失败: {ex.Message}"
            };
        }
    }

    private List<string> ValidateResult(InvestmentSelectionResult result)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(typeof(SelectionType), result.SelectionType))
            errors.Add($"SelectionType 值无效: {result.SelectionType}");

        if (string.IsNullOrWhiteSpace(result.AnalysisSummary))
            errors.Add("AnalysisSummary 不能为空");

        if (string.IsNullOrWhiteSpace(result.MarketEnvironmentAnalysis))
            errors.Add("MarketEnvironmentAnalysis 不能为空");

        if (string.IsNullOrWhiteSpace(result.InvestmentAdvice))
            errors.Add("InvestmentAdvice 不能为空");

        if (result.RiskWarnings == null || result.RiskWarnings.Count == 0)
            errors.Add("RiskWarnings 不能为空");

        for (int i = 0; i < result.Recommendations.Count; i++)
        {
            var rec = result.Recommendations[i];
            if (string.IsNullOrWhiteSpace(rec.Symbol))
                errors.Add($"第{i + 1}个推荐的 Symbol 不能为空");

            if (string.IsNullOrWhiteSpace(rec.Name))
                errors.Add($"第{i + 1}个推荐的 Name 不能为空");

            if (string.IsNullOrWhiteSpace(rec.Reason))
                errors.Add($"第{i + 1}个推荐的 Reason 不能为空");

            if (!Enum.IsDefined(typeof(RiskLevel), rec.RiskLevel))
                errors.Add($"第{i + 1}个推荐的 RiskLevel 值无效: {rec.RiskLevel}");
        }

        return errors;
    }

    private string FormatScreenedStocksForAnalysis(List<ScreenerStockInfo> stocks)
    {
        var simplifiedStocks = stocks.Select(s =>
        {
            var data = new Dictionary<string, object>();

            data["名称"] = s.Name;
            data["代码"] = s.Symbol;

            void AddIfNotZero(string key, decimal value, int decimals = 2, decimal divisor = 1)
            {
                if (value != 0)
                {
                    var convertedValue = divisor != 1 ? value / divisor : value;
                    data[key] = Math.Round(convertedValue, decimals);
                }
            }

            AddIfNotZero("当前价_元", s.Current);
            AddIfNotZero("涨跌幅_百分比", s.Pct);
            AddIfNotZero("当日振幅_百分比", s.ChgPct);
            AddIfNotZero("总市值_亿元", s.Mc, 2, 100000000);
            AddIfNotZero("流通市值_亿元", s.Fmc, 2, 100000000);
            AddIfNotZero("成交额_亿元", s.Amount, 2, 100000000);
            AddIfNotZero("成交量_万股", s.Volume);
            AddIfNotZero("量比", s.VolumeRatio);
            AddIfNotZero("换手率_百分比", s.Tr);
            AddIfNotZero("市盈率TTM", s.PeTtm);
            AddIfNotZero("市盈率LYR", s.PeLyr);
            AddIfNotZero("市净率", s.Pb);
            AddIfNotZero("市销率", s.Psr);
            AddIfNotZero("每股净资产_元", s.Bps);
            AddIfNotZero("每股收益_元", s.Eps);
            AddIfNotZero("股息收益率_百分比", s.DyL);
            AddIfNotZero("净资产收益率ROE_百分比", s.RoeDiluted);
            AddIfNotZero("总资产报酬率_百分比", s.Niota);
            AddIfNotZero("净利润_亿元", s.NetProfit, 2, 100000000);
            AddIfNotZero("营业收入_亿元", s.TotalRevenue, 2, 100000000);
            AddIfNotZero("净利润同比增长_百分比", s.Npay);
            AddIfNotZero("营收同比增长_百分比", s.Oiy);
            AddIfNotZero("近5日涨跌幅_百分比", s.Pct5);
            AddIfNotZero("近10日涨跌幅_百分比", s.Pct10);
            AddIfNotZero("近20日涨跌幅_百分比", s.Pct20);
            AddIfNotZero("近60日涨跌幅_百分比", s.Pct60);
            AddIfNotZero("近120日涨跌幅_百分比", s.Pct120);
            AddIfNotZero("近250日涨跌幅_百分比", s.Pct250);
            AddIfNotZero("年初至今涨跌幅_百分比", s.PctCurrentYear);
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

    private string BuildAnalysisPrompt(InvestmentSelectionWorkflowRequest request, string stocksData)
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

    private string GetAnalysisInstructions(bool isNewsAnalysis)
    {
        return @"
你是专业的投资顾问，基于用户需求/新闻热点和股票数据提供投资建议。

## 核心职责
从筛选出的股票中进行多维度分析，输出结构化推荐报告。

## 评估维度（灵活权重）
1. **财务质量**：ROE、利润增长率、现金流、EPS/BPS
2. **估值水平**：PE/PB/PS 合理性、低估/高估判断、股息率
3. **市场表现**：涨跌幅、流动性（成交额/换手率）、技术面趋势
4. **需求匹配**：风险偏好、投资期限、行业偏好" + (isNewsAnalysis ? "，或新闻关联度" : "") + @"
5. **社交热度**：雪球关注/讨论及增长趋势（辅助参考）

## 分析要点
- 选出最优股票时，优先考虑财务健康度和估值合理性
- 推荐理由必须包含具体数据支撑，避免空泛描述
- 风险提示应针对个股和市场环境的具体风险
- 如无合适标的，可返回空推荐列表

## 输出格式
严格按 JSON Schema 定义的结构输出，所有必填字段不能为空或null。
";
    }

    private InvestmentSelectionResult CreateDefaultResult()
    {
        return new InvestmentSelectionResult
        {
            SelectionType = SelectionType.UserRequest,
            Recommendations = new List<InvestmentRecommendation>(),
            ConfidenceScore = 0,
            AnalysisSummary = "解析分析结果失败",
            MarketEnvironmentAnalysis = "无可用分析",
            InvestmentAdvice = "建议重新尝试分析",
            RiskWarnings = new List<string> { "分析失败，请联系技术支持" }
        };
    }
}

