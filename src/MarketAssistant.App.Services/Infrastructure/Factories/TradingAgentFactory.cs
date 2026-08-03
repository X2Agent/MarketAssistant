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
    AIAgent CreateAgent(
        TradingAuthorizationMode authorizationMode = TradingAuthorizationMode.Disabled,
        Func<string, string, Task<bool>>? confirmationCallback = null);
}

/// <summary>
/// 交易 Agent 工厂实现。<see cref="MarketContext.CurrentMarket"/> 仅影响界面与分析链路；
/// 自主交易与工具解析<strong>始终</strong>使用 <see cref="MarketType.Crypto"/> 的 Keyed 注册（现货 Binance），与当前所选市场无关。
/// </summary>
public class TradingAgentFactory : AgentFactoryBase, ITradingAgentFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TradingAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        TokenTrackingMiddleware tokenTracking,
        ILoggerFactory loggerFactory,
        ILogger<TradingAgentFactory> logger)
        : base(serviceProvider, chatClientFactory, tokenTracking, logger)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 创建包装中间件后的 <see cref="TradingAgent"/>；工具一律从 <see cref="MarketType.Crypto"/> 解析。
    /// </summary>
    public AIAgent CreateAgent(
        TradingAuthorizationMode authorizationMode = TradingAuthorizationMode.Disabled,
        Func<string, string, Task<bool>>? confirmationCallback = null)
    {
        try
        {
            var chatClient = CreateChatClient();
            var tools = ResolveToolsFor(typeof(TradingAgent), MarketType.Crypto);

            var agent = (AIAgent)ActivatorUtilities.CreateInstance(
                ServiceProvider, typeof(TradingAgent), chatClient, tools);

            // 每个 Agent 使用独立守卫，授权模式由调用方显式选择；默认禁止真实交易。
            var guardMiddleware = new TradingFunctionGuardMiddleware(
                _loggerFactory.CreateLogger<TradingFunctionGuardMiddleware>(),
                authorizationMode,
                confirmationCallback);

            // 通过 MAF Builder 模式附加中间件链：Token 追踪 + Function Calling 守卫
            var middlewareAgent = agent
                .AsBuilder()
                .Use(
                    runFunc: guardMiddleware.InvokeRunAsync,
                    runStreamingFunc: guardMiddleware.InvokeRunStreamingAsync)
                .Use(
                    runFunc: _tokenTracking.InvokeAsync,
                    runStreamingFunc: _tokenTracking.InvokeStreamingAsync)
                .Use(guardMiddleware.InvokeAsync)
                .Build();

            Logger.LogInformation("成功创建 TradingAgent（已附加 Token 追踪 + 交易守卫中间件）");
            return middlewareAgent;
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
