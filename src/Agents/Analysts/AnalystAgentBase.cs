using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 分析师代理抽象基类
/// 继承 DelegatingAIAgent，提供统一的 Agent 创建和配置管理
/// </summary>
public abstract class AnalystAgentBase : DelegatingAIAgent
{
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
        IList<AITool> tools)
        : base(CreateInnerAgent(chatClient, instructions, name, description, temperature, topP, topK, responseFormat, tools))
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
        IList<AITool> tools)
    {
        return chatClient.CreateAIAgent(new ChatClientAgentOptions(
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
            }
        });
    }

    public override Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        return base.RunAsync(messages, thread, options, cancellationToken);
    }
}
