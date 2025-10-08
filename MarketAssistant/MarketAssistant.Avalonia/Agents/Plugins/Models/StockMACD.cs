using System.Text.Json.Serialization;

namespace MarketAssistant.Plugins.Models;

public class StockMACD
{
    /// <summary>
    /// 交易时间，短分时级别格式为yyyy-MM-ddHH:mm:ss，日线级别为yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    /// <summary>
    /// DIFF值
    /// </summary>
    [JsonPropertyName("diff")]
    public decimal Diff { get; set; }

    /// <summary>
    /// DEA值
    /// </summary>
    [JsonPropertyName("dea")]
    public decimal Dea { get; set; }

    /// <summary>
    /// MACD值
    /// </summary>
    [JsonPropertyName("macd")]
    public decimal Macd { get; set; }

    /// <summary>
    /// EMA（12）值
    /// </summary>
    [JsonPropertyName("ema12")]
    public decimal Ema12 { get; set; }

    /// <summary>
    /// EMA（26）值
    /// </summary>
    [JsonPropertyName("ema26")]
    public decimal Ema26 { get; set; }
}
