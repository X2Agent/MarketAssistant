using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

public class StockBoll
{
    /// <summary>
    /// 交易时间，短分时级别格式为yyyy-MM-ddHH:mm:ss，日线级别为yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    /// <summary>
    /// 上轨
    /// </summary>
    [JsonPropertyName("u")]
    public decimal? U { get; set; }

    /// <summary>
    /// 下轨
    /// </summary>
    [JsonPropertyName("d")]
    public decimal? D { get; set; }

    /// <summary>
    /// 中轨
    /// </summary>
    [JsonPropertyName("m")]
    public decimal? M { get; set; }
}
