using System.Reflection;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Trading;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
public class TradingAgentFactory : ITradingAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly TokenTrackingMiddleware _tokenTracking;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TradingAgentFactory> _logger;

    public TradingAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        TokenTrackingMiddleware tokenTracking,
        ILoggerFactory loggerFactory,
        ILogger<TradingAgentFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _chatClientFactory = chatClientFactory;
        _tokenTracking = tokenTracking;
        _loggerFactory = loggerFactory;
        _logger = logger;
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
            var chatClient = _chatClientFactory.CreateClient();
            var tools = ResolveToolParameters();

            var agent = (AIAgent)ActivatorUtilities.CreateInstance(
                _serviceProvider, typeof(TradingAgent), chatClient, tools);

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

            _logger.LogInformation("成功创建 TradingAgent（已附加 Token 追踪 + 交易守卫中间件）");
            return middlewareAgent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建 TradingAgent 失败");
            throw;
        }
    }

    private IList<AITool> ResolveToolParameters()
    {
        var toolAttributes = typeof(TradingAgent).GetCustomAttributes<RequiresToolsAttribute>().ToList();
        var tools = new List<AITool>();

        foreach (var attr in toolAttributes)
        {
            var toolService = _serviceProvider.GetKeyedService(attr.ToolInterfaceType, MarketType.Crypto);
            if (toolService is IToolsProvider provider)
            {
                tools.AddRange(provider.GetFunctions());
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
