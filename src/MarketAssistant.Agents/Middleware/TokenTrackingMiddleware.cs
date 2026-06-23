using MarketAssistant.Agents.TokenManagement;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

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

        LogAndAccumulate(session, (int)inputTokens, (int)outputTokens, innerAgent.Name,
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
        int outputCharCount = 0;
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
            {
                outputCharCount += text.Length;
            }

            yield return update;
        }

        // 优先使用精确值，回退到字符估算
        var inputTokens = streamingUsage?.InputTokenCount ?? TokenEstimator.EstimateTotalTokens(messages);
        var outputTokens = streamingUsage?.OutputTokenCount ?? TokenEstimator.EstimateTokens(new string(' ', outputCharCount));

        LogAndAccumulate(session, (int)inputTokens, (int)outputTokens, agentName,
            isPrecise: streamingUsage != null);
    }

    private void LogAndAccumulate(AgentSession? session, int inputTokens, int outputTokens, string? agentName,
        bool isPrecise = false)
    {
        _logger.LogDebug(
            "Token 追踪 [{Agent}] - 输入: {InputTokens}, 输出: {OutputTokens} ({Source})",
            agentName ?? "Unknown", inputTokens, outputTokens,
            isPrecise ? "提供商精确值" : "估算值");

        if (session == null) return;

        var cumulativeInput = session.StateBag.TryGetValue<string>(InputTokensKey, out var existing)
            && int.TryParse(existing, out var existingVal)
            ? existingVal + inputTokens
            : inputTokens;
        var cumulativeOutput = session.StateBag.TryGetValue<string>(OutputTokensKey, out var existingOut)
            && int.TryParse(existingOut, out var existingOutVal)
            ? existingOutVal + outputTokens
            : outputTokens;

        session.StateBag.SetValue(InputTokensKey, cumulativeInput.ToString());
        session.StateBag.SetValue(OutputTokensKey, cumulativeOutput.ToString());
    }

    /// <summary>
    /// 从 Session 的 StateBag 中读取累计 Token 数
    /// </summary>
    public static (int Input, int Output) GetCumulativeTokens(AgentSession? session)
    {
        if (session == null) return (0, 0);

        var input = session.StateBag.TryGetValue<string>(InputTokensKey, out var i) && int.TryParse(i, out var iv) ? iv : 0;
        var output = session.StateBag.TryGetValue<string>(OutputTokensKey, out var o) && int.TryParse(o, out var ov) ? ov : 0;
        return (input, output);
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
