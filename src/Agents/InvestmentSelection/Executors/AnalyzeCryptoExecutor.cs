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
/// 步骤3: AI分析虚拟币筛选结果的 Executor
/// 对筛选出的虚拟币进行深度分析并生成推荐报告
/// </summary>
public sealed class AnalyzeCryptoExecutor : Executor<AssetScreeningResult, InvestmentSelectionResult>
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<AnalyzeCryptoExecutor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public AnalyzeCryptoExecutor(
        IChatClientFactory chatClientFactory,
        ILogger<AnalyzeCryptoExecutor> logger) : base("AnalyzeCrypto")
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
        _logger.LogInformation("[步骤3/3-虚拟币] 对筛选结果进行AI分析");

        try
        {
            var originalRequest = input.OriginalRequest;
            if (originalRequest == null)
            {
                _logger.LogError("[步骤3/3-虚拟币] 缺少原始请求信息");
                return new InvestmentSelectionResult
                {
                    Recommendations = new List<InvestmentRecommendation>(),
                    ConfidenceScore = 0,
                    AnalysisSummary = "分析失败：缺少原始请求信息"
                };
            }

            if (originalRequest.MarketType != MarketType.Crypto)
            {
                throw new InvalidOperationException($"AnalyzeCryptoExecutor 仅支持 Crypto 市场，当前市场类型: {originalRequest.MarketType}");
            }

            if (input.ScreenedAssets.Count == 0)
            {
                _logger.LogWarning("[步骤3/3-虚拟币] 未筛选到符合条件的虚拟币");
                return new InvestmentSelectionResult
                {
                    Recommendations = new List<InvestmentRecommendation>(),
                    ConfidenceScore = 0,
                    AnalysisSummary = "未找到符合条件的虚拟币，建议放宽筛选条件。"
                };
            }

            var cryptoDataText = FormatScreenedCryptoForAnalysis(input.ScreenedAssets);

            var systemPrompt = GetAnalysisInstructions(originalRequest.IsNewsAnalysis);
            var userPrompt = BuildAnalysisPrompt(originalRequest, cryptoDataText);

            var options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schema: AIJsonUtilities.CreateJsonSchema(typeof(InvestmentSelectionResult)),
                    schemaName: "InvestmentSelectionResult",
                    schemaDescription: "投资选择分析结果，包含推荐虚拟币列表和分析报告"),
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

            _logger.LogDebug("[步骤3/3-虚拟币] AI原始响应: {Response}", response.Text);

            var result = JsonSerializer.Deserialize<InvestmentSelectionResult>(response.Text, JsonOptions);

            if (result == null)
            {
                _logger.LogWarning("[步骤3/3-虚拟币] 响应反序列化失败，原始响应: {Response}", response.Text);
                result = CreateDefaultResult();
            }
            else
            {
                var validationErrors = ValidateResult(result);
                if (validationErrors.Count > 0)
                {
                    _logger.LogWarning("[步骤3/3-虚拟币] AI返回数据验证失败: {Errors}", string.Join("; ", validationErrors));
                }

                if (result.Recommendations.Count == 0)
                {
                    _logger.LogWarning("[步骤3/3-虚拟币] AI未生成任何推荐，原始响应: {Response}", response.Text);
                }
            }

            _logger.LogInformation("[步骤3/3-虚拟币] 分析完成，推荐 {Count} 个虚拟币，置信度: {Score}",
                result.Recommendations.Count, result.ConfidenceScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤3/3-虚拟币] AI分析失败");
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

    private string FormatScreenedCryptoForAnalysis(List<ScreenerStockInfo> cryptos)
    {
        // TODO: 当虚拟币筛选服务实现后，这里需要格式化虚拟币特有的数据
        // 目前暂时使用通用格式
        var simplifiedCryptos = cryptos.Select(c =>
        {
            var data = new Dictionary<string, object>
            {
                ["名称"] = c.Name,
                ["代码"] = c.Symbol
            };

            // 虚拟币特有字段（待实现）
            // data["市值_美元"] = ...
            // data["24h交易量_美元"] = ...
            // data["24h涨跌幅_百分比"] = ...
            // data["7天涨跌幅_百分比"] = ...

            return data;
        }).ToList();

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return JsonSerializer.Serialize(simplifiedCryptos, jsonOptions);
    }

    private string BuildAnalysisPrompt(InvestmentSelectionWorkflowRequest request, string cryptoData)
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

            sb.AppendLine();
        }

        sb.AppendLine("## 筛选出的虚拟币数据（JSON格式）");
        sb.AppendLine(cryptoData);
        sb.AppendLine();
        sb.AppendLine("## 分析任务");
        sb.AppendLine("请基于以上虚拟币数据和用户需求，进行综合分析并生成推荐报告。");
        sb.AppendLine("- 从中选择最优的3-8个虚拟币进行推荐");
        sb.AppendLine("- 说明推荐理由和风险提示");

        return sb.ToString();
    }

    private string GetAnalysisInstructions(bool isNewsAnalysis)
    {
        return @"
你是专业的加密货币投资顾问，基于用户需求/新闻热点和虚拟币数据提供投资建议。

## 核心职责
从筛选出的虚拟币中进行多维度分析，输出结构化推荐报告。

## 评估维度（灵活权重）
1. **项目基本面**：技术创新、团队背景、生态发展、实际应用
2. **市场表现**：市值排名、交易量、价格走势、流动性
3. **链上数据**：活跃地址、交易次数、持币集中度、大户动向
4. **社区热度**：社交媒体讨论、开发者活跃度、社区支持
5. **风险评估**：波动性、监管风险、技术风险、市场情绪" + (isNewsAnalysis ? "、新闻关联度" : "") + @"

## 虚拟币特有分析要点
- 优先考虑市值排名前100的主流币种
- 关注项目的技术创新和实际应用场景
- 评估代币经济模型的合理性
- 注意市场情绪和恐慌贪婪指数
- 虚拟币市场波动性大，风险提示要充分

## 分析要点
- 推荐理由必须包含具体数据支撑，避免空泛描述
- 风险提示应特别强调虚拟币的高波动性
- 如无合适标的，可返回空推荐列表

## 输出格式
严格按 JSON Schema 定义的结构输出，所有必填字段不能为空或null。
Symbol 字段格式为交易对形式，如 BTC/USDT、ETH/USDT。
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

