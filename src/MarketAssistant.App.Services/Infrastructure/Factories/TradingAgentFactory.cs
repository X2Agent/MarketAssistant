using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Trading;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 交易 Agent 工厂接口
/// </summary>
public interface ITradingAgentFactory
{
    AIAgent CreateAutomationAgent();
}

/// <summary>
/// 交易 Agent 工厂实现。<see cref="MarketContext.CurrentMarket"/> 仅影响界面与分析链路；
/// 自主交易与工具解析<strong>始终</strong>使用 <see cref="MarketType.Crypto"/> 的 Keyed 注册（现货 Binance），与当前所选市场无关。
/// </summary>
public class TradingAgentFactory : AgentFactoryBase, ITradingAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;

    public TradingAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        TokenTrackingMiddleware tokenTracking,
        ILogger<TradingAgentFactory> logger)
        : base(serviceProvider, tokenTracking, logger)
    {
        _chatClientFactory = chatClientFactory;
    }

    /// <summary>
    /// 创建自动交易使用的 <see cref="TradingAgent"/>；工具一律从 <see cref="MarketType.Crypto"/> 解析。
    /// </summary>
    public AIAgent CreateAutomationAgent()
    {
        try
        {
            var chatClient = _chatClientFactory.CreateClient();
            var tools = ResolveToolsFor(typeof(TradingAgent), MarketType.Crypto);

            var agent = (AIAgent)ActivatorUtilities.CreateInstance(
                ServiceProvider, typeof(TradingAgent), chatClient, tools);

            Logger.LogInformation("成功创建自动交易 Agent（工具执行由 TradeExecutor 统一风控）");
            return WrapWithTokenTracking(agent);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "创建 TradingAgent 失败");
            throw;
        }
    }

    /// <inheritdoc />
    protected override void OnToolMissing(Type agentType, Type toolInterfaceType, MarketType marketType)
    {
        // 交易 Agent 工具缺失视为致命错误，直接抛异常
        throw new InvalidOperationException(
            $"{agentType.Name} 所需的工具 {toolInterfaceType.Name} 未注册（{marketType}）");
    }
}
