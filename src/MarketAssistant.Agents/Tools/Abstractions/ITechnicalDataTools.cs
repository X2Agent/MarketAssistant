using MarketAssistant.Agents.Tools.Models.Technical;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 技术分析数据工具接口
/// </summary>
public interface ITechnicalDataTools : IToolsProvider
{
    /// <summary>
    /// 获取KDJ技术指标
    /// </summary>
    Task<TechnicalKDJ> GetKDJAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取MACD技术指标
    /// </summary>
    Task<TechnicalMACD> GetMACDAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取BOLL技术指标
    /// </summary>
    Task<TechnicalBoll> GetBOLLAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取MA技术指标
    /// </summary>
    Task<TechnicalMA> GetMAAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 K 线历史序列（OHLCV），用于判断趋势方向及多周期一致性
    /// </summary>
    Task<List<OhlcvBar>> GetKLinesAsync(string assetSymbol, string interval = "daily", int count = 30, CancellationToken cancellationToken = default);
}





