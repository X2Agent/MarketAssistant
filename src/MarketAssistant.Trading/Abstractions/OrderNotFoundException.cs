using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Trading.Abstractions;

/// <summary>交易所明确确认指定订单不存在，可与订单状态查询失败区分。</summary>
public sealed class OrderNotFoundException(string message) : FriendlyException(message);

public sealed class PostOnlyWouldTakeException(string message) : FriendlyException(message);
