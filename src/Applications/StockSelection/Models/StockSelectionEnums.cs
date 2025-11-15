using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.StockSelection.Models;

/// <summary>
/// 风险等级枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RiskLevel>))]
public enum RiskLevel
{
    /// <summary>
    /// 低风险
    /// </summary>
    [Description("低风险")]
    Low,

    /// <summary>
    /// 中风险
    /// </summary>
    [Description("中风险")]
    Medium,

    /// <summary>
    /// 高风险
    /// </summary>
    [Description("高风险")]
    High
}

/// <summary>
/// 选股类型枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SelectionType>))]
public enum SelectionType
{
    /// <summary>
    /// 用户需求分析
    /// </summary>
    [Description("用户需求分析")]
    [EnumMember(Value = "user_request")]
    UserRequest,

    /// <summary>
    /// 新闻分析
    /// </summary>
    [Description("新闻分析")]
    [EnumMember(Value = "news_based")]
    NewsBased
}
