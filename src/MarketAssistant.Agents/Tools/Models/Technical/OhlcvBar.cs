using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Technical;

/// <summary>
/// 单根 K 线 OHLCV 数据，用于 AI 工具返回 K 线时间序列
/// </summary>
public sealed class OhlcvBar
{
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [JsonPropertyName("o")]
    public decimal O { get; set; }

    [JsonPropertyName("h")]
    public decimal H { get; set; }

    [JsonPropertyName("l")]
    public decimal L { get; set; }

    [JsonPropertyName("c")]
    public decimal C { get; set; }

    [JsonPropertyName("v")]
    public decimal V { get; set; }
}
