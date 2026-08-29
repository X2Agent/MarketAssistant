using MarketAssistant.Agents.Tools.Models.Technical;

namespace MarketAssistant.Agents.Tools.Abstractions;

public interface ITechnicalDataTools : IToolsProvider
{
    Task<TechnicalKDJ> GetKDJAsync(string assetSymbol, CancellationToken cancellationToken = default);

    Task<TechnicalMACD> GetMACDAsync(string assetSymbol, CancellationToken cancellationToken = default);

    Task<TechnicalBoll> GetBOLLAsync(string assetSymbol, CancellationToken cancellationToken = default);

    Task<TechnicalMA> GetMAAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 K 线历史序列（OHLCV），用于判断趋势方向及多周期一致性
    /// </summary>
    Task<List<OhlcvBar>> GetKLinesAsync(string assetSymbol, string interval = "daily", int count = 30, CancellationToken cancellationToken = default);
}





