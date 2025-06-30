using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Stocks.Models;

public class StockMA
{
    /// <summary>
    /// 交易时间，短分时级别格式为yyyy-MM-ddHH:mm:ss，日线级别为yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    /// <summary>
    /// MA3，没有则为null
    /// </summary>
    [JsonPropertyName("ma3")]
    public decimal? MA3 { get; set; }

    /// <summary>
    /// MA5，没有则为null
    /// </summary>
    [JsonPropertyName("ma5")]
    public decimal? MA5 { get; set; }

    /// <summary>
    /// MA10，没有则为null
    /// </summary>
    [JsonPropertyName("ma10")]
    public decimal? MA10 { get; set; }

    /// <summary>
    /// MA15，没有则为null
    /// </summary>
    [JsonPropertyName("ma15")]
    public decimal? MA15 { get; set; }

    /// <summary>
    /// MA20，没有则为null
    /// </summary>
    [JsonPropertyName("ma20")]
    public decimal? MA20 { get; set; }

    /// <summary>
    /// MA30，没有则为null
    /// </summary>
    [JsonPropertyName("ma30")]
    public decimal? MA30 { get; set; }

    /// <summary>
    /// MA60，没有则为null
    /// </summary>
    [JsonPropertyName("ma60")]
    public decimal? MA60 { get; set; }

    /// <summary>
    /// MA120，没有则为null
    /// </summary>
    [JsonPropertyName("ma120")]
    public decimal? MA120 { get; set; }

    /// <summary>
    /// MA200，没有则为null
    /// </summary>
    [JsonPropertyName("ma200")]
    public decimal? MA200 { get; set; }

    /// <summary>
    /// MA250，没有则为null
    /// </summary>
    [JsonPropertyName("ma250")]
    public decimal? MA250 { get; set; }
}
