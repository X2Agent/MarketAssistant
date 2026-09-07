using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 分析师失败隔离扩展：把分析师 Agent 的运行异常或空输出转换为
/// <see cref="AnalystFailureMessages"/> 失败标记消息，使 Fan-In 聚合器
/// 仍能从每位分析师（含失败者）收到恰好一条消息，实现
/// 「单分析师失败不拖垮整次分析」的降级语义。
/// 调用方取消（外部 CancellationToken）不被拦截，原样向上传播。
/// </summary>
public static class AIAgentFailureIsolation
{
    /// <summary>
    /// 包装分析师 Agent。失败时触发 <paramref name="onAnalysisFailed"/> 回调并返回失败标记消息。
    /// </summary>
    public static AIAgent WithFailureIsolation(
        this AIAgent agent,
        Action<Exception>? onAnalysisFailed = null)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var agentName = agent.Name ?? "UnknownAnalyst";
        return agent
            .AsBuilder()
            .Use(
                runFunc: (messages, session, options, innerAgent, cancellationToken) =>
                    RunGuardedAsync(agentName, onAnalysisFailed, messages, session, options, innerAgent, cancellationToken),
                runStreamingFunc: (messages, session, options, innerAgent, cancellationToken) =>
                    RunStreamingGuarded(agentName, onAnalysisFailed, messages, session, options, innerAgent, cancellationToken))
            .Build();
    }

    private static async Task<AgentResponse> RunGuardedAsync(
        string agentName,
        Action<Exception>? onAnalysisFailed,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await innerAgent.RunAsync(messages, session, options, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(response.Text))
                return response;

            // 空输出与异常同等对待：没有结论的分析师不能假装分析成功
            throw new InvalidOperationException("模型未返回任何文本结论");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            onAnalysisFailed?.Invoke(ex);
            return new AgentResponse(CreateFailureMessage(agentName, ex));
        }
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> RunStreamingGuarded(
        string agentName,
        Action<Exception>? onAnalysisFailed,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var accumulatedText = new StringBuilder();

        await using var enumerator = innerAgent
            .RunStreamingAsync(messages, session, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            var step = await StepAsync(enumerator, accumulatedText, cancellationToken).ConfigureAwait(false);

            if (step.Error != null)
            {
                onAnalysisFailed?.Invoke(step.Error);
                yield return CreateFailureUpdate(agentName, step.Error);
                yield break;
            }

            if (!step.Moved)
                break;

            if (step.Update is not null)
                yield return step.Update;
        }

        if (accumulatedText.Length == 0)
        {
            var error = new InvalidOperationException("模型未返回任何文本结论");
            onAnalysisFailed?.Invoke(error);
            yield return CreateFailureUpdate(agentName, error);
        }
    }

    /// <summary>
    /// 推进流式枚举并把异常带出，避免在迭代器方法内使用 try-catch 包住 yield return。
    /// </summary>
    private static async Task<StreamStep> StepAsync(
        IAsyncEnumerator<AgentResponseUpdate> enumerator,
        StringBuilder accumulatedText,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                return new StreamStep(Moved: false, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new StreamStep(Moved: false, null, ex);
        }

        var update = enumerator.Current;
        if (update.Text is { Length: > 0 } text)
            accumulatedText.Append(text);

        return new StreamStep(Moved: true, update, null);
    }

    private static ChatMessage CreateFailureMessage(string agentName, Exception exception)
        => new(ChatRole.Assistant, AnalystFailureMessages.BuildFailureText(agentName, exception.Message))
        {
            AuthorName = agentName
        };

    private static AgentResponseUpdate CreateFailureUpdate(string agentName, Exception exception)
        => new(ChatRole.Assistant, AnalystFailureMessages.BuildFailureText(agentName, exception.Message))
        {
            AuthorName = agentName
        };

    private sealed record StreamStep(bool Moved, AgentResponseUpdate? Update, Exception? Error);
}
