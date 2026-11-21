using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Trading;

/// <summary>
/// 虚拟币交易决策 Agent：只持有行情、账户与策略的只读工具，分析后输出决策。
/// 下单、撤单、策略状态与护栏的写入一律不暴露给模型，由 AISignalStrategyExecutor 解析决策后
/// 经 TradeExecutor 统一执行（置信度门控、仓位上限、风控、确认），模型无法绕过这些校验。
/// </summary>
[RequiresTools(typeof(ITradingExecutionTools))]
[RequiresTools(typeof(IStrategyTools))]
[RequiresTools(typeof(IBasicDataTools))]
[RequiresTools(typeof(ITechnicalDataTools))]
public class TradingAgent(
    IChatClient chatClient,
    IList<AITool> tools) : DelegatingAIAgent(CreateInnerAgent(chatClient, tools))
{
    private const string AgentName = "TradingAgent";
    private const string AgentDescription = "虚拟币交易决策助手，只读分析市场与账户数据并给出交易决策（下单由系统执行）";

    private static AIAgent CreateInnerAgent(IChatClient chatClient, IList<AITool> tools)
    {
        var functionInvokingClient = new FunctionInvokingChatClient(chatClient)
        {
            MaximumIterationsPerRequest = 20,
            // 工具失败原因（余额/持仓/行情查询错误）对决策至关重要，须对模型可见：据此改为 HOLD 而非凭空决策
            IncludeDetailedErrors = true
        };
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
            },
            UseProvidedChatClientAsIs = true
        };

        return new ChatClientAgent(functionInvokingClient, options);
    }

    private static string BuildSystemPrompt() => """
        你是一个专业的虚拟币交易决策助手。你只负责分析与决策，没有下单、撤单或修改策略的能力；
        你给出决策后，下单、风控与止盈止损护栏均由系统统一执行。

        ## 能力
        - 查询账户余额、持仓和订单状态（GetAccountBalance / GetCurrentPositions / GetOrderStatus）
        - 分析市场数据：价格、K线、技术指标（GetAssetInfo / GetKLineData / CalculateMACD 等）
        - 查询交易策略（GetActiveStrategies / GetStrategy）
        - 根据策略规则和风控约束判断是否应该交易

        ## 决策流程
        1. 收到分析请求时，先获取当前市场数据和技术指标
        2. 结合策略规则评估是否满足交易条件
        3. 检查账户余额和风控限制
        4. 给出决策，并说明完整的推理过程

        ## 约束
        - 决策的输出格式以请求中指定的为准
        - 必须说明每次决策的推理过程
        - 遇到不确定情况，倾向于不交易（宁可错过，不可做错）
        - 不要在没有充分分析的情况下给出交易决策
        - 每次只关注当前请求的交易标的
        """;
}
