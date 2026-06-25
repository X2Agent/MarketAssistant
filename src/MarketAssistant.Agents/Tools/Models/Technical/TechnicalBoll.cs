using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Technical;

[Description("布林带指标，用于判断价格波动区间和突破信号")]
public class TechnicalBoll
{
    [Description("交易时间")]
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [Description("上轨（中轨+2倍标准差），价格触及上轨可能面临压力")]
    [JsonPropertyName("u")]
    public decimal? U { get; set; }

    [Description("下轨（中轨-2倍标准差），价格触及下轨可能获得支撑")]
    [JsonPropertyName("d")]
    public decimal? D { get; set; }

    [Description("中轨（20日移动平均线），趋势方向的参考基准")]
    [JsonPropertyName("m")]
    public decimal? M { get; set; }
}
