using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 现货用户数据流 executionReport 事件：订单状态变更回报。
/// 字段命名对应 Binance 现货用户数据流文档的单字母缩写。
/// </summary>
public class ExecutionReport
{
    [JsonPropertyName("e")] public string EventType { get; set; } = string.Empty;
    [JsonPropertyName("s")] public string Symbol { get; set; } = string.Empty;
    [JsonPropertyName("c")] public string ClientOrderId { get; set; } = string.Empty;
    [JsonPropertyName("S")] public string Side { get; set; } = string.Empty;
    [JsonPropertyName("o")] public string OrderType { get; set; } = string.Empty;
    [JsonPropertyName("i")] public long OrderId { get; set; }
    /// <summary>当前执行类型（NEW/TRADE/CANCELED 等）</summary>
    [JsonPropertyName("x")] public string ExecutionType { get; set; } = string.Empty;
    /// <summary>订单状态（NEW/PARTIALLY_FILLED/FILLED/CANCELED/EXPIRED/REJECTED）</summary>
    [JsonPropertyName("X")] public string OrderStatus { get; set; } = string.Empty;
    [JsonPropertyName("q")] public decimal OrderQuantity { get; set; }
    [JsonPropertyName("p")] public decimal OrderPrice { get; set; }
    /// <summary>本次成交数量</summary>
    [JsonPropertyName("l")] public decimal LastExecutedQty { get; set; }
    /// <summary>本次成交价</summary>
    [JsonPropertyName("L")] public decimal LastExecutedPrice { get; set; }
    /// <summary>累计已成交数量</summary>
    [JsonPropertyName("z")] public decimal CumulativeFilledQty { get; set; }
    /// <summary>累计成交金额</summary>
    [JsonPropertyName("Z")] public decimal CumulativeQuoteQty { get; set; }
    [JsonPropertyName("n")] public decimal Commission { get; set; }
    [JsonPropertyName("N")] public string? CommissionAsset { get; set; }
    [JsonPropertyName("T")] public long TradeTime { get; set; }
}

