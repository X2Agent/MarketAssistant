using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection.Executors;

/// <summary>
/// 统一的资产分析 Executor
/// 对筛选出的资产进行深度分析并生成推荐报告
/// </summary>
public sealed partial class AnalyzeAssetsExecutor : Executor
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnalyzeAssetsExecutor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public AnalyzeAssetsExecutor(
        IChatClientFactory chatClientFactory,
        IServiceProvider serviceProvider,
        ILogger<AnalyzeAssetsExecutor> logger) : base("AnalyzeAssets")
    {
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [MessageHandler]
    private async ValueTask<InvestmentSelectionResult> HandleAsync(
        AssetScreeningResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var originalRequest = input.OriginalRequest;
        if (originalRequest == null)
        {
            _logger.LogError("[步骤3/3] 缺少原始请求信息");
            return CreateDefaultResult("分析失败：缺少原始请求信息");
        }

        _logger.LogInformation("[步骤3/3-{MarketType}] 对筛选结果进行AI分析", originalRequest.MarketType);

        try
        {
            if (input.ScreenedAssets.Count == 0)
            {
                _logger.LogWarning("[步骤3/3-{MarketType}] 未筛选到符合条件的资产", originalRequest.MarketType);
                return CreateDefaultResult("未找到符合条件的资产，建议放宽筛选条件。");
            }

            // 根据市场类型获取对应的数据格式化器
            var formatter = _serviceProvider.GetRequiredKeyedService<IAssetDataFormatter>(originalRequest.MarketType);

            _logger.LogInformation("[步骤3/3-{MarketType}] 使用格式化器: {FormatterType}",
                originalRequest.MarketType, formatter.GetType().Name);

            var assetsDataText = formatter.FormatAssetsForAnalysis(input.ScreenedAssets);
            var systemPrompt = formatter.GetAnalysisInstructions(originalRequest.IsNewsAnalysis);
            var userPrompt = BuildAnalysisPrompt(originalRequest, assetsDataText);

            var options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schema: AIJsonUtilities.CreateJsonSchema(typeof(InvestmentSelectionResult)),
                    schemaName: "InvestmentSelectionResult",
                    schemaDescription: "投资选择分析结果，包含推荐资产列表和分析报告"),
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

            _logger.LogDebug("[步骤3/3-{MarketType}] AI原始响应: {Response}", originalRequest.MarketType, response.Text);

            var result = JsonSerializer.Deserialize<InvestmentSelectionResult>(response.Text, JsonOptions);

            if (result == null)
            {
                _logger.LogWarning("[步骤3/3-{MarketType}] 响应反序列化失败，原始响应: {Response}",
                    originalRequest.MarketType, response.Text);
                result = CreateDefaultResult("解析分析结果失败");
            }
            else
            {
                var validationErrors = ValidateResult(result);
                if (validationErrors.Count > 0)
                {
                    _logger.LogWarning("[步骤3/3-{MarketType}] AI返回数据验证失败: {Errors}",
                        originalRequest.MarketType, string.Join("; ", validationErrors));
                }

                if (result.Recommendations.Count == 0)
                {
                    _logger.LogWarning("[步骤3/3-{MarketType}] AI未生成任何推荐，原始响应: {Response}",
                        originalRequest.MarketType, response.Text);
                }
            }

            _logger.LogInformation("[步骤3/3-{MarketType}] 分析完成，推荐 {Count} 个资产，置信度: {Score}",
                originalRequest.MarketType, result.Recommendations.Count, result.ConfidenceScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤3/3-{MarketType}] AI分析失败", originalRequest.MarketType);
            return CreateDefaultResult($"分析失败: {ex.Message}");
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

    private string BuildAnalysisPrompt(InvestmentSelectionWorkflowRequest request, string assetsData)
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

        string assetType = request.MarketType == Infrastructure.Core.MarketType.Crypto ? "虚拟币" : "股票";
        sb.AppendLine($"## 筛选出的{assetType}数据（JSON格式）");
        sb.AppendLine(assetsData);
        sb.AppendLine();
        sb.AppendLine("## 分析任务");
        sb.AppendLine($"请基于以上{assetType}数据和用户需求，进行综合分析并生成推荐报告。");
        sb.AppendLine($"- 从中选择最优的3-8个{assetType}进行推荐");
        sb.AppendLine("- 说明推荐理由和风险提示");

        return sb.ToString();
    }

    private InvestmentSelectionResult CreateDefaultResult(string message)
    {
        return new InvestmentSelectionResult
        {
            SelectionType = SelectionType.UserRequest,
            Recommendations = new List<InvestmentRecommendation>(),
            ConfidenceScore = 0,
            AnalysisSummary = message,
            MarketEnvironmentAnalysis = "无可用分析",
            InvestmentAdvice = "建议重新尝试分析",
            RiskWarnings = new List<string> { "分析失败，请联系技术支持" }
        };
    }
}
