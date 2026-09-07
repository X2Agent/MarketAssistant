using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安多空比 API 响应模型
/// </summary>
/// <remarks>
/// 通用于三种 API：
/// - globalLongShortAccountRatio (longAccount/shortAccount 表示账户占比)
/// - topLongShortAccountRatio (longAccount/shortAccount 表示大户账户占比)
/// - topLongShortPositionRatio (longAccount/shortAccount 表示大户持仓占比)
/// </remarks>
public class BinanceLongShortRatioResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("longShortRatio")]
    public decimal LongShortRatio { get; set; }

    [JsonPropertyName("longAccount")]
    public decimal LongAccount { get; set; }

    [JsonPropertyName("shortAccount")]
    public decimal ShortAccount { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
