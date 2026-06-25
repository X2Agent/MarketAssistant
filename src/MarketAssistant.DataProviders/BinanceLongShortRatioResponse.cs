using System.Text.Json.Serialization;

namespace MarketAssistant.Services.Data;

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
    public string LongShortRatio { get; set; } = string.Empty;

    [JsonPropertyName("longAccount")]
    public string LongAccount { get; set; } = string.Empty;

    [JsonPropertyName("shortAccount")]
    public string ShortAccount { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
