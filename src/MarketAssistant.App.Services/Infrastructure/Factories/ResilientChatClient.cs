using Microsoft.Extensions.AI;
using Polly;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// IChatClient 装饰器：为所有 LLM 调用附加 Polly 瞬态错误重试管道。
/// 覆盖 Coordinator 和所有业务分析师的 LLM 调用，避免单次瞬态错误导致分析维度缺失。
/// </summary>
internal sealed class ResilientChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ResiliencePipeline _pipeline;

    public ResilientChatClient(IChatClient inner, ResiliencePipeline pipeline)
    {
        _inner = inner;
        _pipeline = pipeline;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await _pipeline.ExecuteAsync(
            async ct => await _inner.GetResponseAsync(messages, options, ct),
            cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // 流式调用不支持简单重试（流一旦开始输出无法回放），直接透传
        return _inner.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        _inner.Dispose();
    }
}
