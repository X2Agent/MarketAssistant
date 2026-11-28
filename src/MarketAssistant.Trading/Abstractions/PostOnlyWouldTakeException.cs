using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Trading.Abstractions;

public sealed class PostOnlyWouldTakeException(string message) : FriendlyException(message);
