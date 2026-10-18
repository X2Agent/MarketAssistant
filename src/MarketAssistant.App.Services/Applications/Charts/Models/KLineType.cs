using System.ComponentModel;

namespace MarketAssistant.Applications.Charts.Models;

/// <summary>
/// K线类型枚举
/// </summary>
public enum KLineType
{
    /// <summary>
    /// 5分钟K线
    /// </summary>
    [Description("5分钟")]
    Minute5,

    /// <summary>
    /// 15分钟K线
    /// </summary>
    [Description("15分钟")]
    Minute15,

    /// <summary>
    /// 日K线
    /// </summary>
    [Description("日线")]
    Daily,

    /// <summary>
    /// 周K线
    /// </summary>
    [Description("周线")]
    Weekly,

    /// <summary>
    /// 月K线
    /// </summary>
    [Description("月线")]
    Monthly
}






