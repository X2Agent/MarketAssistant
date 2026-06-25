using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Technical;

[Description("MACD指标（移动平均收敛/发散），用于判断趋势方向和买卖时机")]
public class TechnicalMACD
{
    [Description("交易时间")]
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [Description("DIFF快线（EMA12与EMA26之差），反映短期动能")]
    [JsonPropertyName("diff")]
    public decimal Diff { get; set; }

    [Description("DEA慢线（DIFF的9日均线），DIFF上穿DEA为金叉")]
    [JsonPropertyName("dea")]
    public decimal Dea { get; set; }

    [Description("MACD柱线（2*(DIFF-DEA)），红柱看多绿柱看空")]
    [JsonPropertyName("macd")]
    public decimal Macd { get; set; }

    [Description("12日指数移动均线")]
    [JsonPropertyName("ema12")]
    public decimal Ema12 { get; set; }

    [Description("26日指数移动均线")]
    [JsonPropertyName("ema26")]
    public decimal Ema26 { get; set; }
}