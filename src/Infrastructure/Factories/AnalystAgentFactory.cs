using MarketAssistant.Agents;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 分析师代理工厂接口
/// </summary>
public interface IAnalystAgentFactory
{
    /// <summary>
    /// 根据 AnalystType 类型创建对应的代理
    /// </summary>
    AIAgent CreateAnalyst(AnalystType analystType);

    /// <summary>
    /// 批量创建分析师代理
    /// </summary>
    List<AIAgent> CreateAnalysts(IEnumerable<AnalystType> analystTypes);
}

/// <summary>
/// 分析师代理工厂实现
/// 负责创建配置好的分析师代理（基于 DelegatingAIAgent 模式）
/// </summary>
public class AnalystAgentFactory : IAnalystAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly StockBasicTools _stockBasicTools;
    private readonly StockFinancialTools _stockFinancialTools;
    private readonly StockTechnicalTools _stockTechnicalTools;
    private readonly GroundingSearchTools _groundingSearchTools;
    private readonly StockNewsTools _newsTools;
    private readonly ILogger<AnalystAgentFactory> _logger;

    public AnalystAgentFactory(
        IChatClientFactory chatClientFactory,
        StockBasicTools stockBasicTools,
        StockFinancialTools stockFinancialTools,
        StockTechnicalTools stockTechnicalTools,
        GroundingSearchTools groundingSearchTools,
        StockNewsTools newsTools,
        ILogger<AnalystAgentFactory> logger)
    {
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _stockBasicTools = stockBasicTools ?? throw new ArgumentNullException(nameof(stockBasicTools));
        _stockFinancialTools = stockFinancialTools ?? throw new ArgumentNullException(nameof(stockFinancialTools));
        _stockTechnicalTools = stockTechnicalTools ?? throw new ArgumentNullException(nameof(stockTechnicalTools));
        _groundingSearchTools = groundingSearchTools ?? throw new ArgumentNullException(nameof(groundingSearchTools));
        _newsTools = newsTools ?? throw new ArgumentNullException(nameof(newsTools));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据 AnalystType 类型创建对应的代理
    /// 每个代理类自己管理配置和工具
    /// </summary>
    public AIAgent CreateAnalyst(AnalystType analystType)
    {
        try
        {
            var chatClient = _chatClientFactory.CreateClient();

            AIAgent agent = analystType switch
            {
                AnalystType.FinancialAnalyst => new FinancialAnalystAgent(
                    chatClient,
                    _stockBasicTools,
                    _stockFinancialTools),

                AnalystType.TechnicalAnalyst => new TechnicalAnalystAgent(
                    chatClient,
                    _stockBasicTools,
                    _stockTechnicalTools),

                AnalystType.FundamentalAnalyst => new FundamentalAnalystAgent(
                    chatClient,
                    _stockBasicTools),

                AnalystType.MarketSentimentAnalyst => new MarketSentimentAnalystAgent(
                    chatClient,
                    _stockFinancialTools),

                AnalystType.NewsEventAnalyst => new NewsEventAnalystAgent(
                    chatClient,
                    _newsTools),

                AnalystType.CoordinatorAnalyst => new CoordinatorAnalystAgent(
                    chatClient,
                    _groundingSearchTools),

                _ => throw new ArgumentException($"Unknown analyst type: {analystType}", nameof(analystType))
            };

            _logger.LogInformation(
                "成功创建分析师代理: {AnalystType}, 实例类型: {AgentType}",
                analystType,
                agent.GetType().Name);

            return agent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建分析师代理时发生错误: {AnalystType}", analystType);
            throw;
        }
    }

    /// <summary>
    /// 批量创建分析师代理
    /// </summary>
    public List<AIAgent> CreateAnalysts(IEnumerable<AnalystType> analystTypes)
    {
        var createdAgents = new List<AIAgent>();

        foreach (var analystType in analystTypes)
        {
            try
            {
                var createdAgent = CreateAnalyst(analystType);
                createdAgents.Add(createdAgent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "跳过创建分析师代理: {AnalystType}", analystType);
            }
        }

        _logger.LogInformation("批量创建分析师代理完成，成功创建: {Count} 个", createdAgents.Count);
        return createdAgents;
    }
}
