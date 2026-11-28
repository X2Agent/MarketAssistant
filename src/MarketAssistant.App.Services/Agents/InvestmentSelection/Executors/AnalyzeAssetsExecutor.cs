using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection.Executors;

/// <summary>
/// 统一的资产分析 Executor
/// 对筛选出的资产进行深度分析并生成推荐报告
/// </summary>
public sealed partial class AnalyzeAssetsExecutor(
    IChatClientFactory chatClientFactory,
    IServiceProvider serviceProvider,
    ILogger<AnalyzeAssetsExecutor> logger) : Executor("AnalyzeAssets")
{
    private readonly IChatClientFactory _chatClientFactory = chatClientFactory;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<AnalyzeAssetsExecutor> _logger = logger;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [MessageHandler]
    private async ValueTask<InvestmentSelectionResult> HandleAsync(
        AssetScreeningResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await AnalyzeAsync(input, cancellationToken);

        // 显式推入工作流输出队列：WithOutputFrom 的 auto-yield 在 RunStreamingAsync 路径下
        // 会晚于事件流结束入队，WorkflowOutputEvent 丢失（同 CoordinatorExecutor）
        await context.YieldOutputAsync(result, cancellationToken);

        return result;
    }

    private async Task<InvestmentSelectionResult> AnalyzeAsync(
        AssetScreeningResult input,
        CancellationToken cancellationToken)
    {
        var originalRequest = input.OriginalRequest
            ?? throw new InvalidOperationException("[步骤3/3] 缺少原始请求信息：上游工作流状态不一致");

        _logger.LogInformation("[步骤3/3-{MarketType}] 对筛选结果进行AI分析", originalRequest.MarketType);

        try
        {
            var appliedCriteria = CriteriaDescriber.Describe(input.Criteria);

            if (input.ScreenedAssets.Count == 0)
            {
                _logger.LogWarning("[步骤3/3-{MarketType}] 未筛选到符合条件的资产", originalRequest.MarketType);
                return CreateEmptyResult(
                    "未找到符合条件的资产，建议放宽筛选条件。",
                    originalRequest.IsNewsAnalysis ? SelectionType.NewsBased : SelectionType.UserRequest,
                    appliedCriteria);
            }

            // 根据市场类型获取对应的数据格式化器
            var formatter = _serviceProvider.GetRequiredKeyedService<IAssetDataFormatter>(originalRequest.MarketType);

            _logger.LogInformation("[步骤3/3-{MarketType}] 使用格式化器: {FormatterType}",
                originalRequest.MarketType, formatter.GetType().Name);

            var assetsDataText = formatter.FormatAssetsForAnalysis(input.ScreenedAssets);
            var systemPrompt = formatter.GetAnalysisInstructions(originalRequest.IsNewsAnalysis);
            var userPrompt = BuildAnalysisPrompt(originalRequest, assetsDataText);

            var runtime = _chatClientFactory.CreateRuntime();
            systemPrompt = StructuredOutputOptions.AppendSchemaInstructions(
                systemPrompt,
                typeof(InvestmentSelectionResult),
                runtime.StructuredOutputMode,
                JsonOptions);

            var options = new ChatOptions
            {
                ResponseFormat = StructuredOutputOptions.CreateResponseFormat(
                    typeof(InvestmentSelectionResult),
                    runtime.StructuredOutputMode,
                    JsonOptions),
                Temperature = 0.2f
            };
            var response = await runtime.Client.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, userPrompt)
                    ],
                    options,
                    cancellationToken);

            _logger.LogDebug(
                "[步骤3/3-{MarketType}] AI 响应接收完成，长度: {ResponseLength}",
                originalRequest.MarketType,
                response.Text?.Length ?? 0);

            var result = LlmJsonExtractor.Deserialize<InvestmentSelectionResult>(response.Text, JsonOptions)
                ?? throw new FriendlyException(
                    $"AI 响应反序列化失败，无法提取投资建议（响应长度 {response.Text?.Length ?? 0}）");

            var validationErrors = ValidateResult(result, input, originalRequest);
            if (validationErrors.Count > 0)
            {
                throw new FriendlyException(
                    $"AI 分析失败: AI 返回的数据不符合约束: {string.Join("; ", validationErrors)}");
            }

            if (result.Recommendations.Count == 0)
            {
                _logger.LogWarning(
                    "[步骤3/3-{MarketType}] AI 未生成任何推荐，响应长度: {ResponseLength}",
                    originalRequest.MarketType,
                    response.Text?.Length ?? 0);
            }

            AttachScreeningQuotes(result, input.ScreenedAssets);
            result.AppliedCriteria = appliedCriteria;

            _logger.LogInformation("[步骤3/3-{MarketType}] 分析完成，推荐 {Count} 个资产，置信度: {Score}",
                originalRequest.MarketType, result.Recommendations.Count, result.ConfidenceScore);

            return result;
        }
        // 取消与已带用户友好信息的 FriendlyException 原样上抛，不二次包装
        catch (Exception ex) when (ex is not (OperationCanceledException or FriendlyException))
        {
            _logger.LogError(ex, "[步骤3/3-{MarketType}] AI分析失败", originalRequest.MarketType);
            throw new FriendlyException($"AI 分析失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 把筛选快照中的现价与涨跌幅回填到推荐项，供界面对照目标价/止损价；
    /// 调用前 <see cref="ValidateResult"/> 已保证每个推荐都存在于筛选结果中。
    /// </summary>
    private static void AttachScreeningQuotes(
        InvestmentSelectionResult result,
        List<ScreenerAssetInfo> screenedAssets)
    {
        var quotesBySymbol = screenedAssets.ToLookup(asset => asset.Symbol, StringComparer.OrdinalIgnoreCase);
        foreach (var recommendation in result.Recommendations)
        {
            var quote = quotesBySymbol[recommendation.Symbol].First();
            recommendation.CurrentPrice = quote.Current;
            recommendation.ChangePercent = quote.Pct;
        }
    }

    internal static List<string> ValidateResult(
        InvestmentSelectionResult result,
        AssetScreeningResult input,
        InvestmentSelectionWorkflowRequest request)
    {
        var errors = StructuredOutputValidator.Validate(result).ToList();
        var expectedSelectionType = request.IsNewsAnalysis
            ? SelectionType.NewsBased
            : SelectionType.UserRequest;

        if (result.SelectionType != expectedSelectionType)
        {
            errors.Add($"SelectionType 应为 {expectedSelectionType}，实际为 {result.SelectionType}");
        }

        if (result.Recommendations is null)
        {
            errors.Add("Recommendations 不能为空");
            return errors;
        }

        var maxRecommendations = Math.Clamp(request.MaxRecommendations, 1, 10);
        if (result.Recommendations.Count > maxRecommendations)
        {
            errors.Add(
                $"推荐数量 {result.Recommendations.Count} 超过请求上限 {maxRecommendations}");
        }

        var availableSymbols = input.ScreenedAssets
            .Select(asset => asset.Symbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recommendedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < result.Recommendations.Count; index++)
        {
            var recommendation = result.Recommendations[index];
            if (!availableSymbols.Contains(recommendation.Symbol))
            {
                errors.Add($"Recommendations[{index}].Symbol 不在筛选结果中: {recommendation.Symbol}");
            }

            if (!recommendedSymbols.Add(recommendation.Symbol))
            {
                errors.Add($"Recommendations[{index}].Symbol 重复: {recommendation.Symbol}");
            }
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
            sb.AppendLine();
        }

        string assetType = request.MarketType == Infrastructure.Core.MarketType.Crypto ? "虚拟币" : "股票";
        sb.AppendLine($"## 筛选出的{assetType}数据（JSON格式）");
        sb.AppendLine(assetsData);
        sb.AppendLine();
        sb.AppendLine("## 分析任务");
        var maxRecommendations = Math.Clamp(request.MaxRecommendations, 1, 10);
        sb.AppendLine($"请基于以上{assetType}数据和用户需求，进行综合分析并生成推荐报告。");
        sb.AppendLine($"- 最多从中选择 {maxRecommendations} 个最优{assetType}进行推荐；没有合适标的时返回空数组");
        sb.AppendLine("- 说明推荐理由和风险提示");

        return sb.ToString();
    }

    /// <summary>
    /// 筛选阶段没有候选时的结果：这是正常的"无匹配"而不是分析失败，不带风险警示。
    /// </summary>
    private static InvestmentSelectionResult CreateEmptyResult(
        string message,
        SelectionType selectionType,
        string appliedCriteria)
    {
        return new InvestmentSelectionResult
        {
            SelectionType = selectionType,
            Recommendations = [],
            ConfidenceScore = 0,
            AnalysisSummary = message,
            MarketEnvironmentAnalysis = "未筛选到候选标的，本次未进行市场环境分析。",
            InvestmentAdvice = "建议放宽筛选条件后重试。",
            RiskWarnings = [],
            AppliedCriteria = appliedCriteria
        };
    }
}
