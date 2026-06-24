using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Technical;

[Description("移动平均线指标，用于判断趋势方向和支撑压力位")]
public class TechnicalMA
{
    [Description("交易时间")]
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [Description("3日均线")]
    [JsonPropertyName("ma3")]
    public decimal? MA3 { get; set; }

    [Description("5日均线（短期趋势）")]
    [JsonPropertyName("ma5")]
    public decimal? MA5 { get; set; }

    [Description("10日均线（短期趋势）")]
    [JsonPropertyName("ma10")]
    public decimal? MA10 { get; set; }

    [Description("15日均线")]
    [JsonPropertyName("ma15")]
    public decimal? MA15 { get; set; }

    [Description("20日均线（中短期趋势）")]
    [JsonPropertyName("ma20")]
    public decimal? MA20 { get; set; }

    [Description("30日均线（中期趋势）")]
    [JsonPropertyName("ma30")]
    public decimal? MA30 { get; set; }

    [Description("60日均线（中期趋势，季线）")]
    [JsonPropertyName("ma60")]
    public decimal? MA60 { get; set; }

    [Description("120日均线（半年线，重要支撑/压力）")]
    [JsonPropertyName("ma120")]
    public decimal? MA120 { get; set; }

    [Description("200日均线（长期趋势分界线）")]
    [JsonPropertyName("ma200")]
    public decimal? MA200 { get; set; }

    [Description("250日均线（年线，牛熊分界参考）")]
    [JsonPropertyName("ma250")]
    public decimal? MA250 { get; set; }
}
