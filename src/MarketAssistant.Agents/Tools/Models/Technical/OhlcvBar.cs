using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Technical;

[Description("单根K线OHLCV数据")]
public sealed class OhlcvBar
{
    [Description("交易时间")]
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [Description("开盘价")]
    [JsonPropertyName("o")]
    public decimal O { get; set; }

    [Description("最高价")]
    [JsonPropertyName("h")]
    public decimal H { get; set; }

    [Description("最低价")]
    [JsonPropertyName("l")]
    public decimal L { get; set; }

    [Description("收盘价")]
    [JsonPropertyName("c")]
    public decimal C { get; set; }

    [Description("成交量")]
    [JsonPropertyName("v")]
    public decimal V { get; set; }
}
