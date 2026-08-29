using MarketAssistant.Applications.AssetScreener.Models;

namespace MarketAssistant.Applications.AssetScreener;

/// <summary>
/// 资产筛选服务接口，支持股票、虚拟币等多种资产类型的筛选
/// </summary>
public interface IAssetScreenerService
{
    /// <summary>
    /// 根据筛选条件筛选资产
    /// </summary>
    /// <param name="criteria">筛选条件对象（支持 StockCriteria、CryptoCriteria 等）</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>筛选结果列表</returns>
    Task<List<ScreenerAssetInfo>> ScreenAsync(object criteria, CancellationToken cancellationToken = default);
}

