using MarketAssistant.Agents.Tools.Models.Technical;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 技术分析数据工具接口
/// </summary>
public interface ITechnicalDataTools
{
    /// <summary>
    /// 获取KDJ技术指标
    /// </summary>
    Task<TechnicalKDJ> GetKDJAsync(string assetSymbol);

    /// <summary>
    /// 获取MACD技术指标
    /// </summary>
    Task<TechnicalMACD> GetMACDAsync(string assetSymbol);

    /// <summary>
    /// 获取BOLL技术指标
    /// </summary>
    Task<TechnicalBoll> GetBOLLAsync(string assetSymbol);

    /// <summary>
    /// 获取MA技术指标
    /// </summary>
    Task<TechnicalMA> GetMAAsync(string assetSymbol);

    /// <summary>
    /// 获取 K 线历史序列（OHLCV），用于判断趋势方向及多周期一致性
    /// </summary>
    Task<List<OhlcvBar>> GetKLinesAsync(string assetSymbol, string interval = "daily", int count = 30);

    /// <summary>
    /// 获取AI工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}





