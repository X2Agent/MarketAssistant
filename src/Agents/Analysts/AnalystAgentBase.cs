using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using static Microsoft.Agents.AI.ChatClientAgentOptions;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 分析师代理抽象基类
/// 继承 DelegatingAIAgent，提供统一的 Agent 创建和配置管理
/// </summary>
public abstract class AnalystAgentBase : DelegatingAIAgent
{
    /// <summary>
    /// 通用数据真实性约束指令
    /// </summary>
    private const string DataIntegrityInstructions = @"
## 数据真实性与反幻觉原则
1. **严格依赖工具数据**：你的所有分析、评估、评分和决策必须严格基于通过工具调用获取的真实数据。
2. **严禁编造数据**：绝对禁止编造数值、捏造事实或臆造不存在的市场情况。如果不知道，就说不知道。
3. **缺失数据处理**：如果工具未能提供所需数据，或者数据不完整，必须在分析结果中明确说明“缺少数据支持”或“数据不可用”，不得进行无依据的猜测或试图掩盖。
4. **拒绝幻觉**：对于未通过工具验证的信息，保持怀疑态度，不要将其作为分析依据。";

    /// <summary>
    /// 初始化分析师代理基类
    /// 子类通过构造函数传递所有配置参数
    /// </summary>
    protected AnalystAgentBase(
        IChatClient chatClient,
        string instructions,
        string name,
        string description,
        float temperature,
        float topP,
        int? topK,
        ChatResponseFormat? responseFormat,
        IList<AITool>? tools,
        Func<AIContextProviderFactoryContext, AIContextProvider>? aiContextProviderFactory = null)
        : base(CreateInnerAgent(chatClient, instructions + DataIntegrityInstructions, name, description,
            temperature, topP, topK, responseFormat, tools, aiContextProviderFactory))
    {
    }

    /// <summary>
    /// 创建内部的 ChatClientAgent
    /// </summary>
    private static AIAgent CreateInnerAgent(
        IChatClient chatClient,
        string instructions,
        string name,
        string description,
        float temperature,
        float topP,
        int? topK,
        ChatResponseFormat? responseFormat,
        IList<AITool>? tools,
        Func<AIContextProviderFactoryContext, AIContextProvider>? aiContextProviderFactory)
    {
        var options = new ChatClientAgentOptions(
            instructions,
            name,
            description)
        {
            ChatOptions = new ChatOptions
            {
                TopK = topK,
                TopP = topP,
                Temperature = temperature,
                Tools = tools,
                ResponseFormat = responseFormat
            },
            AIContextProviderFactory = aiContextProviderFactory
        };

        return chatClient.CreateAIAgent(options);
    }

    public override Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        return base.RunAsync(messages, thread, options, cancellationToken);
    }
}
