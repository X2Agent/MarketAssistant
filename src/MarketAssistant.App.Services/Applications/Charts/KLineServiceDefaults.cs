using MarketAssistant.Applications.Charts.Models;

namespace MarketAssistant.Applications.Charts;

/// <summary>
/// K线服务默认值，根据K线周期返回适合图表展示的默认数据条数
/// </summary>
public static class KLineServiceDefaults
{
    /// <summary>
    /// 根据K线周期返回适合图表展示的默认数据条数
    /// </summary>
    public static int GetDefaultCount(KLineType kLineType) => kLineType switch
    {
        KLineType.Minute5 => 240,   // 约 5 个交易日
        KLineType.Minute15 => 240,  // 约 15 个交易日
        KLineType.Daily => 250,     // 约 1 年（年线）
        KLineType.Weekly => 150,    // 约 3 年
        KLineType.Monthly => 120,   // 约 10 年
        _ => 250
    };
}
