using System.Reflection;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.Trading;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 交易 Agent 工厂接口
/// </summary>
public interface ITradingAgentFactory
{
    TradingAgent CreateAgent();
}

/// <summary>
/// 交易 Agent 工厂实现，固定使用 Crypto 市场类型
/// </summary>
public class TradingAgentFactory : ITradingAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<TradingAgentFactory> _logger;

    public TradingAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        ILogger<TradingAgentFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _chatClientFactory = chatClientFactory;
        _logger = logger;
    }

    public TradingAgent CreateAgent()
    {
        try
        {
            var chatClient = _chatClientFactory.CreateClient();
            var toolParams = ResolveToolParameters();

            var allParams = new object[toolParams.Length + 1];
            allParams[0] = chatClient;
            toolParams.CopyTo(allParams, 1);

            var agent = (TradingAgent)ActivatorUtilities.CreateInstance(
                _serviceProvider, typeof(TradingAgent), allParams);

            _logger.LogInformation("成功创建 TradingAgent");
            return agent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建 TradingAgent 失败");
            throw;
        }
    }

    private object[] ResolveToolParameters()
    {
        var toolAttributes = typeof(TradingAgent).GetCustomAttributes<RequiresToolsAttribute>().ToList();
        var tools = new object[toolAttributes.Count];

        for (var i = 0; i < toolAttributes.Count; i++)
        {
            var attr = toolAttributes[i];
            var toolService = _serviceProvider.GetKeyedService(attr.ToolInterfaceType, MarketType.Crypto);
            if (toolService != null)
            {
                tools[i] = toolService;
            }
            else
            {
                throw new InvalidOperationException(
                    $"TradingAgent 所需的工具 {attr.ToolInterfaceType.Name} 未注册（MarketType.Crypto）");
            }
        }

        return tools;
    }
}
