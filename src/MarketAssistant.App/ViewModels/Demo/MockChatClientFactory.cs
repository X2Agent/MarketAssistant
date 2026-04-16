using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MarketAssistant.ViewModels.Demo;

public class MockChatClientFactory : IChatClientFactory
{
    public IChatClient CreateClient()
    {
        return new MockChatClient();
    }
}

/// <summary>
/// Demo 用 MarketChatSession 工厂，使用 Mock ChatClient
/// </summary>
public class MockMarketChatSessionFactory : IMarketChatSessionFactory
{
    public MarketChatSession Create(string? initialStockCode = null)
    {
        return new MarketChatSession(
            new MockChatClient(),
            NullLogger<MarketChatSession>.Instance,
            initialStockCode: initialStockCode);
    }
}

public class MockChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new ChatClientMetadata("Mock", new Uri("http://localhost"));

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, "Mock Response") }));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
    }
}
