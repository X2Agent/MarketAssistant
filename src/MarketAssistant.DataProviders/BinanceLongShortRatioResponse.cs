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
/// 币安对上述字段恒返回字符串，非空 decimal 依赖该契约；
/// <see cref="StringToDecimalConverter"/> 对 null 抛错，兼任上游 schema 违约哨兵。
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
