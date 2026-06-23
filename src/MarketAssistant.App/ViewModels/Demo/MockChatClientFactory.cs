using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketAssistant.ViewModels.Demo;

/// <summary>
/// 演示用的 MarketChatSession 工厂，使用 MockChatClient 生成模拟回复
/// </summary>
internal class MockMarketChatSessionFactory : IMarketChatSessionFactory
{
    public MarketChatSession Create(string? initialStockCode = null)
    {
        var mockClient = new MockChatClient();
        return new MarketChatSession(
            mockClient,
            NullLogger<MarketChatSession>.Instance,
            initialStockCode: initialStockCode);
    }
}

/// <summary>
/// 模拟聊天客户端，用于演示场景返回固定回复
/// </summary>
internal class MockChatClient : IChatClient
{
    public string Name => nameof(MockChatClient);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "这是演示回复。"));
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return GetStreamingResponseCoreAsync(cancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseCoreAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "这是演示回复。");
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType == typeof(IChatClient) ? this : null;
    }

    public void Dispose() { }
}
