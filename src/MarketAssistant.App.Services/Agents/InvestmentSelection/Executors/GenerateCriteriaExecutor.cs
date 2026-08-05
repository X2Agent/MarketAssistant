using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection.Executors;

/// <summary>
/// 泛型筛选条件生成器
/// 将用户需求或新闻内容转换为结构化的筛选条件
/// </summary>
public sealed class GenerateCriteriaExecutor<TCriteria>
    : Executor<InvestmentSelectionWorkflowRequest, CriteriaGenerationResult>
    where TCriteria : IScreeningCriteria
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ICriteriaGenerationStrategy<TCriteria> _strategy;
    private readonly ILogger<GenerateCriteriaExecutor<TCriteria>> _logger;

    public GenerateCriteriaExecutor(
        IChatClientFactory chatClientFactory,
        ICriteriaGenerationStrategy<TCriteria> strategy,
        ILogger<GenerateCriteriaExecutor<TCriteria>> logger)
        : base($"GenerateCriteria_{strategy.SupportedMarketType}")
    {
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
                Temperature = 0.1f,
                MaxOutputTokens = input.IsNewsAnalysis ? 3500 : 2000
            };

            var response = await runtime.Client.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, userPrompt)
                    ],
                    chatOptions,
                    cancellationToken);

            var criteria = _strategy.DeserializeCriteria(response.Text, input);

            _logger.LogInformation("[步骤1/3-{MarketType}] 筛选条件生成完成，包含 {Count} 个条件",
                _strategy.SupportedMarketType,
                GetCriteriaCount(criteria));

            return new CriteriaGenerationResult
            {
                Criteria = criteria,
                OriginalRequest = input
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤1/3-{MarketType}] 生成筛选条件失败", _strategy.SupportedMarketType);
            if (ex is FriendlyException)
            {
                throw;
            }
            throw new FriendlyException(ex.Message);
        }
    }

    private static int GetCriteriaCount(TCriteria criteria)
    {
        return criteria switch
        {
            StockCriteria stock => stock.Criteria?.Count ?? 0,
            CryptoCriteria crypto => crypto.Criteria?.Count ?? 0,
            _ => 0
        };
    }
}
