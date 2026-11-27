using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 工具驱动 Agent 的输出装饰器：工具闸门 + 收尾结构化调用。
/// 由 <see cref="AnalystAgentBase"/> 在挂载工具时统一装配，分析师与协调分析师共用。
/// </summary>
/// <remarks>
/// <para>
/// JsonObject 服务商的推理后端会把 <c>response_format</c> 当作整段输出的约束解码，与
/// <c>tools</c> 同请求下发时模型无法发出工具调用（OpenRouter 已实测复现：模型把
/// 调用意图写成 JSON 旁白）。因此工具回合不携带 <c>response_format</c>，结构化输出
/// 由本装饰器在工具回合结束后补齐，而不是放弃结构化保证。
/// </para>
/// <para>
/// 闸门：自身工具的调用必须按 CallId 匹配后续成功结果。仅协调器标记的修复请求可以复用入参里的工具往返，
/// 此时本轮可以只改写结论而不再调工具。闸门必须先于收尾调用执行，保证转换的输入是取过数的结论。
/// </para>
/// <para>
/// 收尾调用：最终回复缺可解析 JSON 时，向同一 <see cref="IChatClient"/> 发起一次无工具、
/// 带 <c>response_format</c> 的格式转换调用，把文本结论整理成符合 Schema 的 JSON，提取
/// 干净 JSON 后追加为同作者 Assistant 消息（聚合器同作者取最后一条，协调器取最后一条
/// Assistant 消息，二者都自然读到 JSON）。失败不吞：统一抛出，由失败隔离（分析师）
/// 或协调器修复重试（协调分析师）落点处理。
/// </para>
/// <para>
/// 流式路径：只提供 runFunc，由 MAF 把批量结果回放为流式更新（<c>AIAgentBuilder.Use</c>
/// 的 limited streaming 语义），闸门因此先于任何更新执行。宿主随后按 MessageId 重建响应，
/// 仅当上一条消息也带 MessageId 且二者不同才会切分，所以追加的收尾消息必须带独立 MessageId，
/// 上一条缺省时补齐，否则 JSON 会被并入上一条 Assistant 文本；
/// 收尾调用的用量并入 <see cref="AgentResponse.Usage"/>，由回放作为 UsageContent 转发，
/// 不塞进消息内容。
/// </para>
/// </remarks>
public static class AIAgentToolDrivenOutput
{
    public const string RepairFeedbackKey = "MarketAssistant.ReportRepair";
    private static readonly JsonSerializerOptions ReportOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() }
    };
    /// <summary>收尾调用的格式转换指令；Schema 由 <see cref="StructuredOutputOptions.AppendSchemaInstructions"/> 追加。</summary>
    private const string ConverterInstructions = """
        你是格式转换器。把给定的分析结论整理为一个符合下方 JSON Schema 的合法 JSON 对象。
        仅输出 JSON，不得输出解释文字、思考过程或 JSON 之外的内容；不得增删事实，不得编造数据。
        """;

    /// <summary>
    /// 为挂载工具的 Agent 附加输出装饰器（闸门 + 收尾结构化调用）。
    /// 调用方保证 <paramref name="tools"/> 非空；无工具的 Agent 不应装配本装饰器。
    /// </summary>
    public static AIAgent WithToolDrivenOutput(
        this AIAgent agent,
        IChatClient chatClient,
        Type resultType,
        StructuredOutputMode structuredOutputMode,
        IList<AITool> tools)
    {
        var requiredToolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        // 仅 JsonObject 需要收尾调用：JsonSchema 服务端在最终文本上约束 Schema（可与 tools 共存），
        // Text 模式没有可下发的 response_format。
        var deferStructuredOutput = structuredOutputMode == StructuredOutputMode.JsonObject;
        var schema = deferStructuredOutput ? AIJsonUtilities.CreateJsonSchema(resultType) : default;

        // 只提供 runFunc：流式由 MAF 在批量结果上回放，闸门先于任何更新执行
        return agent.AsBuilder()
            .Use(runFunc: RunAsync, runStreamingFunc: null)
            .Build();

        async Task<AgentResponse> RunAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            AIAgent innerAgent,
            CancellationToken cancellationToken)
        {
            var response = await innerAgent.RunAsync(messages, session, options, cancellationToken)
                .ConfigureAwait(false);

            var repair = messages.LastOrDefault() is { Role: var role, AdditionalProperties: { } properties }
                && role == ChatRole.User && properties.TryGetValue(RepairFeedbackKey, out var value) && value is true;
            if (!HasSuccessfulToolResult(response.Messages) && !(repair && HasSuccessfulToolResult(messages)))
            {
                throw new InvalidOperationException(
                    "未取得本轮分析工具的成功结果，无法生成可信结论");
            }

            var final = response.Messages.LastOrDefault(message => message.Role == ChatRole.Assistant);
            if (final is null || string.IsNullOrWhiteSpace(final.Text))
                throw new InvalidOperationException("模型未返回分析正文");
            if (!deferStructuredOutput || MatchesReport(final.Text, out _))
            {
                return response;
            }

            var structured = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, StructuredOutputOptions.AppendSchemaInstructions(
                        ConverterInstructions, resultType, structuredOutputMode)),
                    new ChatMessage(ChatRole.User, final.Text)
                ],
                new ChatOptions
                {
                    ResponseFormat = StructuredOutputOptions.CreateResponseFormat(resultType, structuredOutputMode),
                    Temperature = 0
                },
                cancellationToken).ConfigureAwait(false);

            if (!MatchesReport(structured.Text, out var json))
            {
                var preview = structured.Text is { Length: > 200 } ? structured.Text[..200] : structured.Text;
                throw new InvalidOperationException($"收尾结构化调用未返回合法 JSON: {preview}");
            }

            // 宿主按 MessageId 重建响应，仅当上一条消息也带 MessageId 且二者不同才会切分，否则把 JSON
            // 并入上一条 Assistant 文本；部分服务商不返回消息 ID，故两条都要有。
            // 收尾调用的用量并入 response.Usage，由 MAF 回放时作为 UsageContent 转发。
            final.MessageId ??= Guid.NewGuid().ToString("N");
            response.Messages.Add(new ChatMessage(ChatRole.Assistant, json)
            {
                AuthorName = final.AuthorName,
                MessageId = Guid.NewGuid().ToString("N")
            });
            if (structured.Usage is not null)
            {
                response.Usage ??= new UsageDetails();
                response.Usage.Add(structured.Usage);
            }

            return response;
        }

        bool MatchesReport(string? text, out string json)
        {
            if (!LlmJsonExtractor.TryExtractJson(text, out json)) return false;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var names = root.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (schema.TryGetProperty("properties", out var properties) && !properties.EnumerateObject().Any(p => names.Contains(p.Name))) return false;
            if (schema.TryGetProperty("required", out var required) && required.EnumerateArray().Any(p => !names.Contains(p.GetString()!))) return false;
            try { return root.Deserialize(resultType, ReportOptions) != null; }
            catch (JsonException) { return false; }
        }

        bool HasSuccessfulToolResult(IEnumerable<ChatMessage> history)
        {
            var calls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in history)
            {
                if (message.Role == ChatRole.Assistant)
                    foreach (var call in message.Contents.OfType<FunctionCallContent>())
                        if (requiredToolNames.Contains(call.Name)) calls.Add(call.CallId);
                if (message.Role == ChatRole.Tool)
                    foreach (var result in message.Contents.OfType<FunctionResultContent>())
                        if (calls.Remove(result.CallId) && result.Exception == null) return true;
            }
            return false;
        }
    }
}
