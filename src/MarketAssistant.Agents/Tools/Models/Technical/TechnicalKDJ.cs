using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Technical;

[Description("KDJ随机指标，用于判断超买超卖和趋势")]
public class TechnicalKDJ
{
    [Description("交易时间")]
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [Description("K值（快速随机值，0-100，高于80为超买，低于20为超卖）")]
    [JsonPropertyName("k")]
    public decimal? K { get; set; }

    [Description("D值（K值的平滑线，0-100，K线上穿D线为金叉买入信号）")]
    [JsonPropertyName("d")]
    public decimal? D { get; set; }

    [Description("J值（3K-2D），大于100为强烈超买，小于0为强烈超卖")]
    [JsonPropertyName("j")]
    public decimal? J { get; set; }
}