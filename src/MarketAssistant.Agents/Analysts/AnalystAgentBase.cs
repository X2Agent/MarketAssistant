using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 分析师代理抽象基类
/// 继承 DelegatingAIAgent，提供统一的 Agent 创建和配置管理
/// </summary>
public abstract class AnalystAgentBase(
    IChatClient chatClient,
    string instructions,
    string name,
    string description,
    float temperature,
    float topP,
    int? topK,
    Type resultType,
    StructuredOutputMode structuredOutputMode,
    IList<AITool>? tools,
    AIContextProvider[]? aiContextProviders = null) : DelegatingAIAgent(CreateInnerAgent(chatClient, instructions + DataIntegrityInstructions, name, description,
            temperature, topP, topK, resultType, structuredOutputMode, tools, aiContextProviders))
{
    /// <summary>
    /// 通用数据真实性约束指令
    /// </summary>
    private const string DataIntegrityInstructions = @"
## 数据真实性与反幻觉原则
1. **严格依赖工具数据**：你的所有分析、评估、评分和决策必须严格基于通过工具调用获取的真实数据。
2. **严禁编造数据**：绝对禁止编造数值、捏造事实或臆造不存在的市场情况。如果不知道，就说不知道。
3. **缺失数据处理**：如果工具未能提供所需数据，或者数据不完整，必须在分析结果中明确说明""缺少数据支持""或""数据不可用""，不得进行无依据的猜测或试图掩盖。
4. **拒绝幻觉**：对于未通过工具验证的信息，保持怀疑态度，不要将其作为分析依据。
5. **先取数后结论**：必须先通过工具调用完成取数，全部工具调用结束后才输出最终结论；过程输出不得伪装成最终结论。";

    /// <summary>
    /// 初始化分析师代理基类（从 YAML 配置读取 name、description、temperature、topP、topK、instructions）
    /// </summary>
    protected AnalystAgentBase(
        IChatClient chatClient,
        AnalystPromptConfig config,
        Type resultType,
        StructuredOutputMode structuredOutputMode,
        IList<AITool>? tools,
        AIContextProvider[]? aiContextProviders = null)
        : this(chatClient, config.Instructions, config.Name, config.Description,
              config.Temperature, config.TopP, config.TopK,
              resultType, structuredOutputMode, tools, aiContextProviders)
    {
    }

    private static AIAgent CreateInnerAgent(
        IChatClient chatClient,
        string instructions,
        string name,
        string description,
        float temperature,
        float topP,
        int? topK,
        Type resultType,
        StructuredOutputMode structuredOutputMode,
        IList<AITool>? tools,
        AIContextProvider[]? aiContextProviders)
    {
        // 空列表等价于无工具，只在此判定一次：下面推迟 response_format 与末尾装配输出装饰器都以它为准。
        var activeTools = tools is { Count: > 0 } ? tools : null;

        // JsonObject 服务商的推理后端会把 response_format 当作整段输出的约束解码，与 tools
        // 同请求下发会压制工具调用（OpenRouter 实测复现）。两段式：工具回合不携带
        // response_format，最终回复缺 JSON 时由 AIAgentToolDrivenOutput 的收尾调用补结构化。
        var deferStructuredOutput = activeTools is not null && structuredOutputMode == StructuredOutputMode.JsonObject;

        instructions = StructuredOutputOptions.AppendSchemaInstructions(
            instructions,
            resultType,
            structuredOutputMode);
        var responseFormat = deferStructuredOutput
            ? null
            : StructuredOutputOptions.CreateResponseFormat(
                resultType,
                structuredOutputMode);
        var options = new ChatClientAgentOptions
        {
            Name = name,
            Description = description,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                TopK = topK,
                TopP = topP,
                Temperature = temperature,
                Tools = tools,
                ResponseFormat = responseFormat
            },
            AIContextProviders = aiContextProviders
        };

        var agent = new ChatClientAgent(chatClient, options);

        // 工具失败原因必须对模型可见：MAF 自动插入的 FunctionInvokingChatClient 默认只回
        // "Error: Function failed."，开启详情后模型能读到 FriendlyException 文案，
        // 据此换参数重试或在结论中如实标注数据不可用（见 DataIntegrityInstructions）。
        // 管道可能在 FIC 外还有别的包装层（如 ApprovalResponseBindingChatClient），
        // 须用 GetService 沿管道向内解析——MAF 内部注入工具走的就是同一路径。
        // 往返上限刻意保持 MAF 默认的 40：分析师要遍历多个数据工具，模型若逐个发起调用，
        // 往返数会逼近工具数，收紧会截断正常分析；单次运行失控由 TokenTracking 的运行级熔断事后兜底。
        if (agent.ChatClient.GetService(typeof(FunctionInvokingChatClient)) is FunctionInvokingChatClient fic)
            fic.IncludeDetailedErrors = true;

        // 挂工具的 Agent 统一附加输出装饰器（闸门 + 收尾结构化），
        // 位于管道最内层：ChatClientAgent → ToolDrivenOutput → TokenTracking → FailureIsolation。
        return activeTools is null
            ? agent
            : agent.WithToolDrivenOutput(chatClient, resultType, structuredOutputMode, activeTools);
    }
}
