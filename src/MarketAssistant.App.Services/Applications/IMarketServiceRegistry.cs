using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Applications.News;

namespace MarketAssistant.Applications;

/// <summary>
/// 市场服务注册表：按市场类型解析各市场差异化实现（Keyed Service 的具名门面）。
/// 消费方（ViewModel 等）通过本接口替代裸 Func 委托或 IServiceProvider 服务定位。
/// </summary>
public interface IMarketServiceRegistry
{
    IKLineService GetKLineService(MarketType marketType);

    IAssetInfoService GetAssetInfoService(MarketType marketType);

    INewsUpdateService GetNewsUpdateService(MarketType marketType);

    IHomeAssetService GetHomeAssetService(MarketType marketType);

    IAssetHistoryService GetAssetHistoryService(MarketType marketType);

    IFavoriteService GetFavoriteService(MarketType marketType);

    IAssetCacheService GetAssetCacheService(MarketType marketType);
}
