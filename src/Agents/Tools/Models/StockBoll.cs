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

    /// <summary>
    /// 数据的自然语言描述，辅助大模型理解
    /// </summary>
    public string Description => $"日期: {T}, 上轨(Upper): {U}, 中轨(Middle): {M}, 下轨(Lower): {D}";
}
