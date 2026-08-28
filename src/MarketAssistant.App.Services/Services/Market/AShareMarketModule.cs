using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Applications.News;
using MarketAssistant.Applications.Telegrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Market;

/// <summary>
/// A 股市场模块——集中注册 A 股所有 Keyed 服务及工作流组件。
/// </summary>
public sealed class AShareMarketModule : IMarketModule
{
    public MarketType MarketType => MarketType.AShare;

    public void Register(IServiceCollection services)
    {
        // 市场能力
        services.AddKeyedSingleton<IMarketCapability, AShareMarketCapability>(MarketType.AShare);

        // Agent 工具
        services.AddKeyedSingleton<IShareBasicTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<IBasicDataTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<IShareFinancialTools, AShareFinancialTools>(MarketType.AShare);
        services.AddKeyedSingleton<IFinancialTools, AShareFinancialTools>(MarketType.AShare);
        services.AddKeyedSingleton<ITechnicalDataTools, AShareTechnicalTools>(MarketType.AShare);
        services.AddKeyedSingleton<INewsDataTools, AShareNewsTools>(MarketType.AShare);
        services.AddKeyedSingleton<IShareSentimentTools, AShareSentimentTools>(MarketType.AShare);
        services.AddKeyedSingleton<ISentimentTools, AShareSentimentTools>(MarketType.AShare);

        // 快讯 & 新闻
        services.AddKeyedSingleton<ITelegramService, AShareTelegramService>(MarketType.AShare);
        services.AddKeyedSingleton<INewsUpdateService>(
            MarketType.AShare,
            (sp, _) => new NewsUpdateService(
                sp.GetRequiredKeyedService<ITelegramService>(MarketType.AShare),
                sp.GetRequiredService<ILogger<NewsUpdateService>>()));

        // 资产服务
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IHomeAssetService, HomeAssetService>(MarketType.AShare);
        services.AddKeyedSingleton<IFavoriteService, FavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetHistoryService, AssetHistoryService>(MarketType.AShare);
        services.AddKeyedSingleton<IKLineService, AShareKLineService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetCacheService, AssetCacheService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetScreenerService, StockScreenerService>(MarketType.AShare);

        // 工作流
        services.AddKeyedSingleton<IAssetDataFormatter, StockDataFormatter>(MarketType.AShare);
        services.AddSingleton<ICriteriaGenerationStrategy<StockCriteria>, StockCriteriaGenerationStrategy>();
        // Transient：由投资选择工作流在每次 Run 内重新解析，避免并发共享状态
        services.AddTransient<GenerateCriteriaExecutor<StockCriteria>>();
    }
}
