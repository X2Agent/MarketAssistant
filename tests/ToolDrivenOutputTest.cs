using System.Runtime.CompilerServices;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TestMarketAssistant;

/// <summary>
/// 工具驱动 Agent 两段式输出契约测试（AIAgentToolDrivenOutput）：
/// JsonObject 模式下工具回合不携带 response_format（后端会把它当作整段输出约束、压制工具调用）、
/// 必须调用过自身工具（闸门，先于收尾调用；入参里的工具往返同样算数）、
/// 最终回复缺 JSON 时由无工具收尾调用补结构化。
/// 流式由 MAF 回放批量结果：收尾消息带独立 MessageId（宿主按 MessageId 重建响应），
/// 收尾调用用量并入 AgentResponse.Usage。
/// </summary>
[TestClass]
public sealed class ToolDrivenOutputTest
{
    /// <summary>用量脚本三次模型调用之和：输入 100 + 150 + 11，输出 10 + 30 + 7。</summary>
    private const long ExpectedTotalInputTokens = 261;

    private const long ExpectedTotalOutputTokens = 47;

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_ToolTurnsOmitResponseFormat_CompliantJsonPassesThrough()
    {
        var client = new ScriptedChatClient(
        [
            () => ToolCallResponse(),
            () => TextResponse("""```json {"score":5,"note":"结论"} ```"""),
        ]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]);

        Assert.HasCount(2, client.CapturedOptions, "守约 JSON 应直接透传，不发起收尾调用");
        Assert.IsTrue(client.CapturedOptions.All(options => options!.ResponseFormat is null),
            "JsonObject 模式下挂工具的工具回合不得携带 response_format");
        Assert.IsTrue(client.CapturedOptions.All(options => options!.Tools is { Count: 1 }));
        Assert.IsTrue(LlmJsonExtractor.TryExtractJson(response.Text, out _), "最终结论应含可解析 JSON");
        Assert.IsTrue(
            response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any(),
            "响应应保留工具调用往返记录");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_TextConclusion_TriggersToolFreeStructuringCall()
    {
        var client = new ScriptedChatClient(
        [
            () => ToolCallResponse(),
            () => TextResponse("比亚迪值得关注，风险中等，建议观望。"),
            () => StructuredResponse("""转换结果：{"score":5,"note":"结论"} 完毕""", 11, 7),
        ]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]);

        Assert.HasCount(3, client.CapturedOptions, "文本结论应触发一次收尾结构化调用");
        var tailOptions = client.CapturedOptions[2]!;
        Assert.IsTrue(ReferenceEquals(tailOptions.ResponseFormat, ChatResponseFormat.Json),
            "收尾调用必须携带 json_object 格式约束");
        Assert.IsTrue(tailOptions.Tools is not { Count: > 0 }, "收尾调用不得携带工具");

        var tailMessages = client.CapturedMessages[2];
        Assert.AreEqual(ChatRole.System, tailMessages[0].Role);
        Assert.Contains("Schema", tailMessages[0].Text!, "收尾调用的系统消息应包含 Schema");
        Assert.AreEqual(ChatRole.User, tailMessages[1].Role);
        Assert.Contains("比亚迪值得关注", tailMessages[1].Text!);

        var appended = response.Messages.Last();
        Assert.AreEqual("""{"score":5,"note":"结论"}""", appended.Text, "追加消息应为提取后的干净 JSON");
        Assert.AreEqual(response.Messages[^2].AuthorName, appended.AuthorName, "追加消息必须保持同作者");
        Assert.IsFalse(string.IsNullOrEmpty(appended.MessageId), "追加消息必须带独立 MessageId，宿主才不会把它并入上一条文本");
        Assert.AreNotEqual(response.Messages[^2].MessageId, appended.MessageId);
        Assert.IsFalse(appended.Contents.OfType<UsageContent>().Any(), "用量走 AgentResponse.Usage，不塞进消息内容");
        Assert.IsNotNull(response.Usage, "收尾调用的用量必须并入 AgentResponse.Usage");
        Assert.AreEqual(11L, response.Usage.InputTokenCount);
        Assert.AreEqual(7L, response.Usage.OutputTokenCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_NeverCallsTool_FailsFastWithoutStructuringCall()
    {
        var client = new ScriptedChatClient([() => TextResponse("跳过工具，直接给出结论。")]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]));

        Assert.Contains("没有调用任何工具", exception.Message);
        Assert.HasCount(1, client.CapturedOptions, "闸门必须先于收尾调用拦截");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonSchemaWithTools_SingleRequestKeepsResponseFormat()
    {
        var client = new ScriptedChatClient(
        [
            () => ToolCallResponse(),
            () => TextResponse("""{"score":6,"note":"结论"}"""),
        ]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonSchema);

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]);

        Assert.HasCount(2, client.CapturedOptions, "JsonSchema 服务端约束最终文本，无需收尾调用");
        Assert.IsTrue(client.CapturedOptions.All(options => options!.ResponseFormat is not null),
            "JsonSchema 模式单次请求应同时携带 tools 与 response_format");
        Assert.Contains("score", response.Text!);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonSchemaWithTools_NeverCallsTool_StillBlockedByGate()
    {
        var client = new ScriptedChatClient([() => TextResponse("跳过工具，直接给出结论。")]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonSchema);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]));

        Assert.Contains("没有调用任何工具", exception.Message);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task WithoutTools_NoGate_SingleRequestKeepsResponseFormat()
    {
        var client = new ScriptedChatClient([() => TextResponse("无工具时的直接回复。")]);
        var agent = new ProbeAnalystAgent(client, tools: null, StructuredOutputMode.JsonObject);

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "你好")]);

        Assert.HasCount(1, client.CapturedOptions);
        Assert.IsTrue(ReferenceEquals(client.CapturedOptions[0]!.ResponseFormat, ChatResponseFormat.Json),
            "无工具的结构化调用仍单次携带 response_format");
        Assert.AreEqual("无工具时的直接回复。", response.Text);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(false, DisplayName = "服务商不返回消息 ID")]
    [DataRow(true, DisplayName = "服务商返回消息 ID")]
    public async Task JsonObjectWithTools_StreamingRebuild_KeepsStructuredJsonAsSeparateLastMessage(
        bool providerReturnsMessageIds)
    {
        ChatResponse Tag(ChatResponse response, string messageId)
            => providerReturnsMessageIds ? WithMessageId(response, messageId) : response;

        var client = new ScriptedChatClient(
        [
            () => Tag(ToolCallResponse(), "chatcmpl-1"),
            () => Tag(TextResponse("比亚迪值得关注，风险中等，建议观望。"), "chatcmpl-2"),
            () => StructuredResponse("""转换结果：{"score":5,"note":"结论"} 完毕""", 11, 7),
        ]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var updates = new List<AgentResponseUpdate>();
        await CollectUpdatesAsync(agent, updates);
        // 工作流宿主正是这样按 MessageId 把流式更新重建为响应，再交给下游执行器
        var rebuilt = updates.ToAgentResponse();

        Assert.HasCount(3, client.CapturedOptions, "工具往返两轮 + 收尾调用一次");
        // 内层 3 条（工具调用、工具结果、文本结论）+ 收尾 JSON 1 条
        Assert.HasCount(4, rebuilt.Messages, "收尾 JSON 必须是独立的一条，不得并入上一条文本结论");
        var last = rebuilt.Messages[^1];
        Assert.AreEqual(ChatRole.Assistant, last.Role);
        Assert.AreEqual("""{"score":5,"note":"结论"}""", last.Text, "最后一条 Assistant 必须是纯 JSON");
        Assert.AreEqual("比亚迪值得关注，风险中等，建议观望。", rebuilt.Messages[^2].Text, "文本结论应原样保留");
        Assert.AreEqual(rebuilt.Messages[^2].AuthorName, last.AuthorName, "追加消息必须保持同作者");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_NonStreaming_UsageEqualsToolRoundsPlusStructuringCall()
    {
        var client = new ScriptedChatClient(CreateUsageScript());
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]);

        Assert.IsNotNull(response.Usage);
        Assert.AreEqual(ExpectedTotalInputTokens, response.Usage.InputTokenCount);
        Assert.AreEqual(ExpectedTotalOutputTokens, response.Usage.OutputTokenCount);
        Assert.AreEqual(ExpectedTotalInputTokens + ExpectedTotalOutputTokens, response.Usage.TotalTokenCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_Streaming_UsageEqualsToolRoundsPlusStructuringCall()
    {
        var client = new ScriptedChatClient(CreateUsageScript());
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var updates = new List<AgentResponseUpdate>();
        await CollectUpdatesAsync(agent, updates);

        // TokenTracking 的流式路径从更新里的 UsageContent 取用量，回放必须把 Usage 无损转发
        var usages = updates.SelectMany(update => update.Contents).OfType<UsageContent>()
            .Select(usage => usage.Details).ToList();
        Assert.AreEqual(ExpectedTotalInputTokens, usages.Sum(usage => usage.InputTokenCount ?? 0));
        Assert.AreEqual(ExpectedTotalOutputTokens, usages.Sum(usage => usage.OutputTokenCount ?? 0));
        Assert.AreEqual(ExpectedTotalInputTokens + ExpectedTotalOutputTokens, usages.Sum(usage => usage.TotalTokenCount ?? 0));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_InputAlreadyHasOwnToolCall_ZeroCallsThisRound_PassesGate()
    {
        // 协调器修复重试：首轮的工具往返随输入带回，本轮只改写结论而不再调工具
        var client = new ScriptedChatClient([() => TextResponse("""{"score":7,"note":"修正后的结论"}""")]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var response = await agent.RunAsync(CreateConversationWithPriorToolCall("get_probe_data"));

        Assert.HasCount(1, client.CapturedOptions, "入参已含自身工具调用时闸门放行，且合规 JSON 无需收尾调用");
        Assert.Contains("修正后的结论", response.Text!);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_InputOnlyHasForeignToolCall_StillBlockedByGate()
    {
        var client = new ScriptedChatClient([() => TextResponse("跳过工具，直接给出结论。")]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => agent.RunAsync(CreateConversationWithPriorToolCall("other_tool")));

        Assert.Contains("没有调用任何工具", exception.Message);
        Assert.HasCount(1, client.CapturedOptions, "闸门必须先于收尾调用拦截");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task JsonObjectWithTools_StreamingPath_GateThrowsBeforeAnyUpdate()
    {
        var client = new ScriptedChatClient([() => TextResponse("跳过工具，直接给出结论。")]);
        var agent = CreateAgent(client, StructuredOutputMode.JsonObject);

        var updates = new List<AgentResponseUpdate>();
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => CollectUpdatesAsync(agent, updates));

        Assert.Contains("没有调用任何工具", exception.Message);
        Assert.IsEmpty(updates, "闸门失败时不得产出任何部分更新");
        Assert.HasCount(1, client.CapturedOptions, "闸门必须先于收尾调用拦截");
    }

    private static IReadOnlyList<Func<ChatResponse>> CreateUsageScript() =>
    [
        () => WithUsage(ToolCallResponse(), 100, 10),
        () => WithUsage(TextResponse("比亚迪值得关注，风险中等，建议观望。"), 150, 30),
        () => StructuredResponse("""转换结果：{"score":5,"note":"结论"} 完毕""", 11, 7),
    ];

    private static async Task CollectUpdatesAsync(AIAgent agent, List<AgentResponseUpdate> updates)
    {
        await foreach (var update in agent.RunStreamingAsync(
                           [new ChatMessage(ChatRole.User, "分析 600519")],
                           session: null,
                           options: null,
                           default))
        {
            updates.Add(update);
        }
    }

    /// <summary>已含一轮工具往返的对话：模拟协调器修复重试携带首轮消息再次调用。</summary>
    private static List<ChatMessage> CreateConversationWithPriorToolCall(string toolName) =>
    [
        new ChatMessage(ChatRole.User, "分析 600519"),
        new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("prior-call", toolName, new Dictionary<string, object?> { ["symbol"] = "600519" })
        ]),
        new ChatMessage(ChatRole.Tool, [new FunctionResultContent("prior-call", "探针数据：600519 现价 1500 元")]),
        new ChatMessage(ChatRole.User, "上一轮结论缺字段，请修正后重新输出。"),
    ];

    private static ProbeAnalystAgent CreateAgent(ScriptedChatClient client, StructuredOutputMode mode)
        => new(client, [CreateProbeTool()], mode);

    private static AIFunction CreateProbeTool() => AIFunctionFactory.Create(
        (string symbol) => "探针数据：600519 现价 1500 元",
        name: "get_probe_data",
        description: "测试用数据工具");

    private static ChatResponse ToolCallResponse()
    {
        var message = new ChatMessage(ChatRole.Assistant, string.Empty)
        {
            Contents =
            {
                new FunctionCallContent("call-1", "get_probe_data",
                    new Dictionary<string, object?> { ["symbol"] = "600519" })
            }
        };
        return new ChatResponse(message);
    }

    private static ChatResponse TextResponse(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    private static ChatResponse StructuredResponse(string text, long inputTokens, long outputTokens)
        => WithUsage(TextResponse(text), inputTokens, outputTokens);

    private static ChatResponse WithMessageId(ChatResponse response, string messageId)
    {
        response.Messages[0].MessageId = messageId;
        return response;
    }

    private static ChatResponse WithUsage(ChatResponse response, long inputTokens, long outputTokens)
    {
        response.Usage = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            TotalTokenCount = inputTokens + outputTokens
        };
        return response;
    }

    /// <summary>探针分析师：直接经 AnalystAgentBase 构造内层管道（不经工厂）。</summary>
    private sealed class ProbeAnalystAgent(
        IChatClient chatClient,
        IList<AITool>? tools,
        StructuredOutputMode mode) : AnalystAgentBase(
            chatClient,
            "探针指令：先调用工具取数，再给出符合 Schema 的 JSON 结论。",
            "ProbeAnalyst",
            "探针",
            0.1f,
            0.9f,
            topK: null,
            typeof(ProbeResult),
            mode,
            tools)
    {
    }

    private sealed class ProbeResult
    {
        public int Score { get; set; }
        public string Note { get; set; } = "";
    }

    /// <summary>按脚本顺序回复并记录每次请求的 IChatClient 桩。</summary>
    private sealed class ScriptedChatClient(IReadOnlyList<Func<ChatResponse>> script) : IChatClient
    {
        private int _index;

        public List<ChatOptions?> CapturedOptions { get; } = [];

        public List<IReadOnlyList<ChatMessage>> CapturedMessages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedMessages.Add([.. messages]);
            CapturedOptions.Add(options);
            return Task.FromResult(script[_index++]());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedMessages.Add([.. messages]);
            CapturedOptions.Add(options);
            await Task.Yield();
            var response = script[_index++]();
            var message = response.Messages[0];
            var update = new ChatResponseUpdate(message.Role, message.Text ?? string.Empty);
            foreach (var content in message.Contents)
            {
                if (content is not TextContent)
                    update.Contents.Add(content);
            }

            if (response.Usage is not null)
                update.Contents.Add(new UsageContent(response.Usage));

            yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
