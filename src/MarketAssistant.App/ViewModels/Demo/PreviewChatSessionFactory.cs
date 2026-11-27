using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace MarketAssistant.ViewModels.Demo;

/// <summary>
/// 卡片预览页专用的会话工厂：会话背后是固定回复的假客户端，不会调用任何真实模型。
/// </summary>
internal sealed class PreviewChatSessionFactory : IMarketChatSessionFactory
{
    public MarketChatSession Create(string? initialStockCode = null)
        => new(new PreviewChatClient(), NullLogger<MarketChatSession>.Instance, initialStockCode: initialStockCode);
}

/// <summary>
/// 固定回复的假聊天客户端；流式回复带一小段延迟，便于观察「思考中 → 输出」的过渡。
/// </summary>
internal sealed class PreviewChatClient : IChatClient
{
    private const string ReplyText = "这是卡片预览页的固定回复，不会调用真实模型。";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ReplyText)));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, ReplyText);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
