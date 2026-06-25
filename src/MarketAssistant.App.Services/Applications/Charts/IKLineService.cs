using MarketAssistant.Applications.Charts.Models;

namespace MarketAssistant.Applications.Charts;

/// <summary>
/// K线数据服务接口（统一接口，支持多种时间周期）
/// </summary>
public interface IKLineService
{
    /// <summary>
    /// 获取K线数据
    /// </summary>
    /// <param name="code">资产代码</param>
    /// <param name="kLineType">K线类型（时间周期）</param>
    /// <param name="count">数据条数</param>
    /// <returns>K线数据列表</returns>
    Task<List<KLineData>> GetKLineDataAsync(string code, KLineType kLineType, int count = 250);
}






