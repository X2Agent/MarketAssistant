using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;

namespace MarketAssistant.Agents.InvestmentSelection.Executors;

/// <summary>
/// 泛型筛选条件生成器
/// 将用户需求或新闻内容转换为结构化的筛选条件
/// </summary>
public sealed class GenerateCriteriaExecutor<TCriteria>(
    IChatClientFactory chatClientFactory,
    ICriteriaGenerationStrategy<TCriteria> strategy,
    ILogger<GenerateCriteriaExecutor<TCriteria>> logger)
    : Executor<InvestmentSelectionWorkflowRequest, CriteriaGenerationResult>($"GenerateCriteria_{strategy.SupportedMarketType}")
    where TCriteria : IScreeningCriteria
{
    private readonly IChatClientFactory _chatClientFactory = chatClientFactory;
    private readonly ICriteriaGenerationStrategy<TCriteria> _strategy = strategy;
    private readonly ILogger<GenerateCriteriaExecutor<TCriteria>> _logger = logger;

    private static readonly JsonSerializerOptions CriteriaLogOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public override async ValueTask<CriteriaGenerationResult> HandleAsync(
        InvestmentSelectionWorkflowRequest input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (input.MarketType != _strategy.SupportedMarketType)
        {
            throw new InvalidOperationException(
                $"GenerateCriteriaExecutor<{typeof(TCriteria).Name}> 仅支持 {_strategy.SupportedMarketType} 市场，当前市场类型: {input.MarketType}");
        }

        _logger.LogInformation("[步骤1/3-{MarketType}] 将{Type}转换为筛选条件",
            _strategy.SupportedMarketType,
            input.IsNewsAnalysis ? "新闻内容" : "用户需求");

        try
        {
            string systemPrompt = input.IsNewsAnalysis
                ? _strategy.BuildNewsAnalysisSystemPrompt()
                : _strategy.BuildUserRequirementSystemPrompt();

            string userPrompt = _strategy.BuildUserPrompt(input);

            var runtime = _chatClientFactory.CreateRuntime();
            systemPrompt = StructuredOutputOptions.AppendSchemaInstructions(
                systemPrompt,
                typeof(TCriteria),
                runtime.StructuredOutputMode);

            var chatOptions = new ChatOptions
            {
                ResponseFormat = StructuredOutputOptions.CreateResponseFormat(
                    typeof(TCriteria),
                    runtime.StructuredOutputMode),
                Temperature = 0.1f
            };

            var response = await runtime.Client.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, userPrompt)
                    ],
                    chatOptions,
                    cancellationToken);

            var criteria = _strategy.DeserializeCriteria(response.Text);
            var result = new CriteriaGenerationResult
            {
                Criteria = criteria,
                OriginalRequest = input
            };

            // 记录完整条件 JSON：结果为空或不符预期时，先看模型把需求译成了什么条件
            _logger.LogInformation("[步骤1/3-{MarketType}] 筛选条件生成完成，包含 {Count} 个条件: {Criteria}",
                _strategy.SupportedMarketType,
                result.CriteriaCount,
                JsonSerializer.Serialize(criteria, CriteriaLogOptions));

            return result;
        }
        // 取消与已带用户友好信息的 FriendlyException 原样上抛，不二次包装
        catch (Exception ex) when (ex is not (OperationCanceledException or FriendlyException))
        {
            _logger.LogError(ex, "[步骤1/3-{MarketType}] 生成筛选条件失败", _strategy.SupportedMarketType);
            throw new FriendlyException($"生成筛选条件失败: {ex.Message}", ex);
        }
    }
}
