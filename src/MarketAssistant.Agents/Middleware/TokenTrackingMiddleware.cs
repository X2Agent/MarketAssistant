using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace MarketAssistant.Agents.Middleware;

/// <summary>
/// Token 追踪中间件，拦截 Agent 运行以估算并记录输入/输出 Token 用量。
/// 通过 agent.AsBuilder().Use(runFunc:, runStreamingFunc:).Build() 附加到任意 AIAgent。
/// </summary>
public sealed class TokenTrackingMiddleware
{
    /// <summary>
    /// AgentSession.StateBag 中累计 Token 数的键
    /// </summary>
    public const string InputTokensKey = "middleware:cumulativeInputTokens";
    public const string OutputTokensKey = "middleware:cumulativeOutputTokens";

    /// <summary>
    /// 单个 Agent 会话累计 Token 上限，超过后抛出异常终止执行，防止工具调用循环失控。
    /// 正常多轮对话约消耗 30k~80k tokens，此值设为 200k 以只拦截真正的死循环。
    /// </summary>
    public const int MaxCumulativeTokens = 200_000;

    private readonly ILogger _logger;

    public TokenTrackingMiddleware(ILogger<TokenTrackingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 非流式 Agent Run 中间件
    /// </summary>
    public async Task<AgentResponse> InvokeAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        var response = await innerAgent.RunAsync(messages, session, options, cancellationToken)
            .ConfigureAwait(false);

        // 优先使用 MAF 原生 UsageContent（由 LLM 提供商返回的精确值）
        var usage = ExtractUsage(response.Messages);
        var inputTokens = usage?.InputTokenCount ?? TokenEstimator.EstimateTotalTokens(messages);
        var outputTokens = usage?.OutputTokenCount ?? TokenEstimator.EstimateTotalTokens(response.Messages);

        LogAndAccumulate(session, inputTokens, outputTokens, innerAgent.Name,
            isPrecise: usage != null);

        return response;
    }

    /// <summary>
    /// 流式 Agent Run 中间件
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> InvokeStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outputText = new StringBuilder();
        UsageDetails? streamingUsage = null;
        var agentName = innerAgent.Name ?? "Unknown";

        _logger.LogInformation("[{Agent}] 开始流式 LLM 调用", agentName);

        await foreach (var update in innerAgent.RunStreamingAsync(messages, session, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            // 尝试从流式更新中提取精确 Usage
            if (update.Contents?.OfType<UsageContent>().FirstOrDefault() is { } usageContent)
            {
                streamingUsage = usageContent.Details;
            }

            if (update.Text is { Length: > 0 } text)
                outputText.Append(text);

            yield return update;
        }

        // 优先使用精确值；无 Usage 时对真实输出文本估算，避免用空格字符严重低估。
        var inputTokens = streamingUsage?.InputTokenCount ?? TokenEstimator.EstimateTotalTokens(messages);
        var outputTokens = streamingUsage?.OutputTokenCount ?? TokenEstimator.EstimateTokens(outputText.ToString());

        LogAndAccumulate(session, inputTokens, outputTokens, agentName,
            isPrecise: streamingUsage != null);
    }

    internal void LogAndAccumulate(AgentSession? session, long inputTokens, long outputTokens, string? agentName,
        bool isPrecise = false)
    {
        _logger.LogDebug(
            "Token 追踪 [{Agent}] - 输入: {InputTokens}, 输出: {OutputTokens} ({Source})",
            agentName ?? "Unknown", inputTokens, outputTokens,
            isPrecise ? "提供商精确值" : "估算值");

        var activity = System.Diagnostics.Activity.Current;
        if (activity?.Source.Name == MarketAssistantDiagnostics.SourceName)
        {
            activity.SetTag("gen_ai.usage.input_tokens", inputTokens);
            activity.SetTag("gen_ai.usage.output_tokens", outputTokens);
            activity.SetTag("marketassistant.token_usage.precise", isPrecise);
        }

        if (session == null) return;

        lock (session)
        {
            var cumulativeInput = session.StateBag.TryGetValue<string>(InputTokensKey, out var existing)
                && long.TryParse(existing, out var existingVal)
                ? checked(existingVal + inputTokens)
                : inputTokens;
            var cumulativeOutput = session.StateBag.TryGetValue<string>(OutputTokensKey, out var existingOut)
                && long.TryParse(existingOut, out var existingOutVal)
                ? checked(existingOutVal + outputTokens)
                : outputTokens;

            session.StateBag.SetValue(InputTokensKey, cumulativeInput.ToString());
            session.StateBag.SetValue(OutputTokensKey, cumulativeOutput.ToString());

            // 熔断：累计 Token 超过上限时抛出异常，终止 Agent 执行，防止工具调用循环失控
            var total = checked(cumulativeInput + cumulativeOutput);
            if (total > MaxCumulativeTokens)
            {
                _logger.LogWarning(
                    "Token 熔断触发 [{Agent}] - 累计 {Total} 超过上限 {Limit}（输入 {In}, 输出 {Out}）",
                    agentName ?? "Unknown", total, MaxCumulativeTokens, cumulativeInput, cumulativeOutput);
                throw new InvalidOperationException(
                    $"Agent 累计 Token 用量 {total} 超过熔断上限 {MaxCumulativeTokens}，已终止执行以防止工具调用循环失控");
            }
        }
    }

    /// <summary>
    /// 从 Session 的 StateBag 中读取累计 Token 数
    /// </summary>
    public static (long Input, long Output) GetCumulativeTokens(AgentSession? session)
    {
        if (session == null) return (0, 0);

        lock (session)
        {
            var input = session.StateBag.TryGetValue<string>(InputTokensKey, out var i) && long.TryParse(i, out var iv) ? iv : 0;
            var output = session.StateBag.TryGetValue<string>(OutputTokensKey, out var o) && long.TryParse(o, out var ov) ? ov : 0;
            return (input, output);
        }
    }

    /// <summary>
    /// 从响应消息中提取 UsageDetails（优先使用 LLM 提供商返回的精确 Token 用量）
    /// </summary>
    private static UsageDetails? ExtractUsage(IEnumerable<ChatMessage>? messages)
    {
        if (messages == null) return null;

        foreach (var message in messages)
        {
            if (message.Contents?.OfType<UsageContent>().FirstOrDefault() is { } usageContent)
            {
                return usageContent.Details;
            }
        }

        return null;
    }
}
