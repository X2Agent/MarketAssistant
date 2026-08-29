using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Applications.News;
using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.Applications;

/// <summary>
/// 基于 Keyed Service 的市场服务注册表实现，解析逻辑与市场模块（AShareMarketModule / CryptoMarketModule）的注册一一对应。
/// </summary>
public sealed class MarketServiceRegistry : IMarketServiceRegistry
{
    private readonly IServiceProvider _serviceProvider;

    public MarketServiceRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IKLineService GetKLineService(MarketType marketType)
        => _serviceProvider.GetRequiredKeyedService<IKLineService>(marketType);

    public IAssetInfoService GetAssetInfoService(MarketType marketType)
        => _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketType);

    public INewsUpdateService GetNewsUpdateService(MarketType marketType)
        => _serviceProvider.GetRequiredKeyedService<INewsUpdateService>(marketType);

    public IHomeAssetService GetHomeAssetService(MarketType marketType)
        => _serviceProvider.GetRequiredKeyedService<IHomeAssetService>(marketType);

    public IAssetHistoryService GetAssetHistoryService(MarketType marketType)
        => _serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(marketType);

    public IFavoriteService GetFavoriteService(MarketType marketType)
        => _serviceProvider.GetRequiredKeyedService<IFavoriteService>(marketType);

    public IAssetCacheService GetAssetCacheService(MarketType marketType)
        => _serviceProvider.GetRequiredKeyedService<IAssetCacheService>(marketType);
}
