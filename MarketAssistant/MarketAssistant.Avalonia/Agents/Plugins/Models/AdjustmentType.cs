using System.Runtime.Serialization;

namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 除权除息处理方式（复权方式）
/// </summary>
public enum AdjustmentType
{
    /// <summary>
    /// 不复权（默认）
    /// </summary>
    [EnumMember(Value = "n")]
    None = 0,

    /// <summary>
    /// 前复权（现金流贴现法）
    /// </summary>
    [EnumMember(Value = "f")]
    Forward = 1,

    /// <summary>
    /// 后复权
    /// </summary>
    [EnumMember(Value = "b")]
    Backward = 2,

    /// <summary>
    /// 等比前复权（维持价格比例关系）
    /// </summary>
    [EnumMember(Value = "fr")]
    RatioForward = 3,

    /// <summary>
    /// 等比后复权（维持价格比例关系）
    /// </summary>
    [EnumMember(Value = "br")]
    RatioBackward = 4
}
