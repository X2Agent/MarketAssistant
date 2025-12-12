using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
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
