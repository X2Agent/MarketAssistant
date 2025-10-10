namespace MarketAssistant.Applications.Stocks.Models;

/// <summary>
/// K线类型枚举
/// </summary>
public enum KLineType
{
    /// <summary>
    /// 5分钟K线
    /// </summary>
    Minute5,
    
    /// <summary>
    /// 15分钟K线
    /// </summary>
    Minute15,
    
    /// <summary>
    /// 日K线
    /// </summary>
    Daily,
    
    /// <summary>
    /// 周K线
    /// </summary>
    Weekly,
    
    /// <summary>
    /// 月K线
    /// </summary>
    Monthly
}
