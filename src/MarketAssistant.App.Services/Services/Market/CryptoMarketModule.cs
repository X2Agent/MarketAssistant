using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Crypto;
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
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Services.Trading.Exchanges;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 虚拟币市场模块——集中注册虚拟币所有 Keyed 服务及工作流组件。
/// </summary>
public sealed class CryptoMarketModule : IMarketModule
{
    public MarketType MarketType => MarketType.Crypto;

    public void Register(IServiceCollection services)
    {
        // 市场能力
        services.AddKeyedSingleton<IMarketCapability, CryptoMarketCapability>(MarketType.Crypto);

        // Agent 工具
        services.AddKeyedSingleton<IBasicDataTools, CryptoBasicTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IFinancialTools, CryptoMetricsTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ITechnicalDataTools, CryptoTechnicalTools>(MarketType.Crypto);
        services.AddKeyedSingleton<INewsDataTools, CryptoNewsTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ISentimentTools, CryptoSentimentTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ITradingExecutionTools, CryptoTradingExecutionTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IStrategyTools, CryptoStrategyTools>(MarketType.Crypto);

        // 快讯 & 新闻
        services.AddKeyedSingleton<ITelegramService, CryptoTelegramService>(MarketType.Crypto);
        services.AddKeyedSingleton<INewsUpdateService>(
            MarketType.Crypto,
            (sp, _) => new NewsUpdateService(
                sp.GetRequiredKeyedService<ITelegramService>(MarketType.Crypto),
                sp.GetRequiredService<ILogger<NewsUpdateService>>()));

        // 资产服务
        services.AddKeyedSingleton<IRealtimeQuoteService, CryptoRealtimeQuoteService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);
        services.AddKeyedSingleton<IHomeAssetService, HomeAssetService>(MarketType.Crypto);
        services.AddKeyedSingleton<IFavoriteService, FavoriteService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetHistoryService, AssetHistoryService>(MarketType.Crypto);
        services.AddKeyedSingleton<IKLineService, CryptoKLineService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetCacheService, AssetCacheService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetScreenerService, CryptoScreenerService>(MarketType.Crypto);

        // 工作流
        services.AddKeyedSingleton<IAssetDataFormatter, CryptoDataFormatter>(MarketType.Crypto);
        services.AddSingleton<ICriteriaGenerationStrategy<CryptoCriteria>, CryptoCriteriaGenerationStrategy>();
        // Transient：由投资选择工作流在每次 Run 内重新解析，避免并发共享状态
        services.AddTransient<GenerateCriteriaExecutor<CryptoCriteria>>();

        // 交易所客户端
        services.AddKeyedSingleton<IExchangeClient>(
            MarketType.Crypto,
            (sp, _) => sp.GetRequiredService<RoutingExchangeClient>());
    }
}
