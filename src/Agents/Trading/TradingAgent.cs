using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Trading;

/// <summary>
/// 虚拟币自主交易 Agent，持有交易专用工具集，接收 Monitor 信号后自主分析并决策
/// </summary>
[RequiresTools(typeof(ITradingExecutionTools))]
[RequiresTools(typeof(IStrategyTools))]
[RequiresTools(typeof(IBasicDataTools))]
[RequiresTools(typeof(ITechnicalDataTools))]
public class TradingAgent : DelegatingAIAgent
{
    private const string AgentName = "TradingAgent";
    private const string AgentDescription = "虚拟币自主交易助手，能分析市场数据并执行交易决策";

    public TradingAgent(
        IChatClient chatClient,
        ITradingExecutionTools tradingTools,
        IStrategyTools strategyTools,
        IBasicDataTools basicTools,
        ITechnicalDataTools technicalTools)
        : base(CreateInnerAgent(chatClient,
            [.. tradingTools.GetFunctions(),
             .. strategyTools.GetFunctions(),
             .. basicTools.GetFunctions(),
             .. technicalTools.GetFunctions()]))
    {
    }

    private static AIAgent CreateInnerAgent(IChatClient chatClient, IList<AITool> tools)
    {
        var options = new ChatClientAgentOptions
        {
            Name = AgentName,
            Description = AgentDescription,
            ChatOptions = new ChatOptions
            {
                Instructions = BuildSystemPrompt(),
                Temperature = 0.1f,
                TopP = 0.1f,
                Tools = tools
            }
        };

        return new ChatClientAgent(chatClient, options);
    }

    private static string BuildSystemPrompt() => """
        你是一个专业的虚拟币自主交易助手。

        ## 能力
        - 查询账户余额和持仓（GetAccountBalance / GetCurrentPositions）
        - 分析市场数据：价格、K线、技术指标（GetAssetInfo / GetKLineData / CalculateMACD 等）
        - 查询和管理交易策略（GetActiveStrategies / UpdateStrategyStatus）
        - 根据策略规则和风控约束决定是否交易
        - 执行买卖操作（PlaceOrder）并记录决策推理

        ## 决策流程
        1. 收到分析请求时，先获取当前市场数据和技术指标
        2. 结合策略规则评估是否满足交易条件
        3. 检查账户余额和风控限制
        4. 决定是否执行交易，并记录完整的推理过程

        ## 约束
        - 所有交易必须经过风控检查（PlaceOrder 内部自动调用）
        - 必须记录每次决策的推理过程
        - 遇到不确定情况，倾向于不交易（宁可错过，不可做错）
        - 不要在没有充分分析的情况下执行交易
        - 每次只关注当前请求的交易对
        """;
}
