using System.Reflection;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Trading;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 交易 Agent 工厂接口
/// </summary>
public interface ITradingAgentFactory
{
    AIAgent CreateAgent();
}

/// <summary>
/// 交易 Agent 工厂实现，固定使用 Crypto 市场类型
/// </summary>
public class TradingAgentFactory : ITradingAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly TokenTrackingMiddleware _tokenTracking;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TradingAgentFactory> _logger;

    /// <summary>
    /// Human-in-the-Loop 确认回调。
    /// 参数: (functionName, argsDescription) → true=放行 false=拒绝。
    /// UI 层可在创建工厂后设置此属性以接入用户确认对话框。
    /// </summary>
    public Func<string, string, Task<bool>>? TradeConfirmationCallback { get; set; }

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

    public AIAgent CreateAgent()
    {
        try
        {
            var chatClient = _chatClientFactory.CreateClient();
            var toolParams = ResolveToolParameters();

            var allParams = new object[toolParams.Length + 1];
            allParams[0] = chatClient;
            toolParams.CopyTo(allParams, 1);

            var agent = (AIAgent)ActivatorUtilities.CreateInstance(
                _serviceProvider, typeof(TradingAgent), allParams);

            // 创建 Function Calling 守卫中间件（每次 CreateAgent 新建实例以重置调用计数）
            var guardMiddleware = new TradingFunctionGuardMiddleware(
                _loggerFactory.CreateLogger<TradingFunctionGuardMiddleware>());
            guardMiddleware.ConfirmationCallback = TradeConfirmationCallback;

            // 通过 MAF Builder 模式附加中间件链：Token 追踪 + Function Calling 守卫
            var middlewareAgent = agent
                .AsBuilder()
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
