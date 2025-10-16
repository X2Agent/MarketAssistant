using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

public class StockKDJ
{
    /// <summary>
    /// 交易时间，短分时级别格式为yyyy-MM-dd HH:mm:ss，日线级别为yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    /// <summary>
    /// K值
    /// </summary>
    [JsonPropertyName("k")]
    public decimal? K { get; set; }

    /// <summary>
    /// D值
    /// </summary>
    [JsonPropertyName("d")]
    public decimal? D { get; set; }

    /// <summary>
    /// J值
    /// </summary>
    [JsonPropertyName("j")]
    public decimal? J { get; set; }
}
