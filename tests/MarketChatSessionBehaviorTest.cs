using System.Runtime.CompilerServices;
using MarketAssistant.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant;

[TestClass]
public sealed class MarketChatSessionBehaviorTest
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task SendMessageStream_NormalMultiTurn_ShouldSendOnlyNewUserMessageToClient()
    {
        using var client = new RecordingChatClient();
        using var session = new MarketChatSession(
            client,
            NullLogger<MarketChatSession>.Instance);

        await DrainAsync(session.SendMessageStreamAsync("第一问"));
        await DrainAsync(session.SendMessageStreamAsync("第二问"));

        Assert.HasCount(2, client.Requests);
        CollectionAssert.AreEqual(new[] { "第一问" }, GetNonSystemTexts(client.Requests[0]));
        CollectionAssert.AreEqual(
            new[] { "第一问", "完整回复", "第二问" },
            GetNonSystemTexts(client.Requests[1]));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task SendMessageStream_CancelledRun_ShouldMarkUiHistoryAndAllowNextTurn()
    {
        using var client = new RecordingChatClient(cancelFirstStream: true);
        using var session = new MarketChatSession(
            client,
            NullLogger<MarketChatSession>.Instance);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in session.SendMessageStreamAsync("会被取消"))
            {
            }
        });

        var interruptedHistory = await session.GetConversationHistoryAsync();
        Assert.HasCount(2, interruptedHistory);
        Assert.AreEqual("会被取消", interruptedHistory[0].Text);
        Assert.AreEqual("部分回复\n\n[回复被中断]", interruptedHistory[1].Text);
        Assert.IsFalse(session.IsProcessing);

        var completedText = await DrainAsync(session.SendMessageStreamAsync("下一问"));
        Assert.AreEqual("完整回复", completedText);
        Assert.HasCount(2, client.Requests);
        CollectionAssert.AreEqual(
            new[] { "会被取消", "部分回复\n\n[回复被中断]", "下一问" },
            GetNonSystemTexts(client.Requests[1]));
    }

    private static string[] GetNonSystemTexts(IReadOnlyList<ChatMessage> messages)
    {
        return messages
            .Where(message => message.Role != ChatRole.System)
            .Select(message => message.Text ?? string.Empty)
            .ToArray();
    }

    private static async Task<string> DrainAsync(IAsyncEnumerable<string> source)
    {
        var builder = new System.Text.StringBuilder();
        await foreach (var item in source)
            builder.Append(item);
        return builder.ToString();
    }

    private sealed class RecordingChatClient(bool cancelFirstStream = false) : IChatClient
    {
        private int _streamCount;

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = messages.Select(CloneMessage).ToList();
            Requests.Add(request);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "完整回复")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = messages.Select(CloneMessage).ToList();
            Requests.Add(request);
            var currentStream = Interlocked.Increment(ref _streamCount);

            if (cancelFirstStream && currentStream == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "部分回复");
                throw new OperationCanceledException(cancellationToken);
            }

            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "完整回复");
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose()
        {
        }

        private static ChatMessage CloneMessage(ChatMessage message)
        {
            return new ChatMessage(message.Role, message.Text ?? string.Empty)
            {
                AuthorName = message.AuthorName
            };
        }
    }
}
