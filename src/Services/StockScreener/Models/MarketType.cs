using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace MarketAssistant.Services.StockScreener.Models;

/// <summary>
/// 市场类型枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MarketType>))]
public enum MarketType
{
    /// <summary>
    /// 全部A股
    /// </summary>
    [Description("全部A股")]
    [EnumMember(Value = "全部A股")]
    AllAShares,

    /// <summary>
    /// 沪市A股
    /// </summary>
    [Description("沪市A股")]
    [EnumMember(Value = "沪市A股")]
    ShanghaiAShares,

    /// <summary>
    /// 深市A股
    /// </summary>
    [Description("深市A股")]
    [EnumMember(Value = "深市A股")]
    ShenzhenAShares
}
