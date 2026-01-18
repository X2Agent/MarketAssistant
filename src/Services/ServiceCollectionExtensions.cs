using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.InvestmentSelection;
using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Applications.News;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.InvestmentSelection;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Extensions;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Cache;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Home;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MarketAssistant.Services;

/// <summary>
/// 服务注册扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册应用程序所有服务
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 注册基础服务
        services.AddHttpClient();
        services.AddMemoryCache();

        // 注册用户设置服务为单例
        services.AddSingleton<IUserSettingService, UserSettingService>();

        // 注册市场上下文服务为单例
        services.AddSingleton<MarketContext>();

        // 注册通用工具（不依赖市场类型）
        services.AddSingleton<GroundingSearchTools>();

        // 注册 Agent Tools - A股实现（Keyed Services）
        services.AddKeyedSingleton<IShareBasicTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<IBasicDataTools, AShareBasicTools>(MarketType.AShare); // 注册为基接口
        services.AddKeyedSingleton<IShareFinancialTools, AShareFinancialTools>(MarketType.AShare);
        services.AddKeyedSingleton<IFinancialTools, AShareFinancialTools>(MarketType.AShare); // 注册为基接口
        services.AddKeyedSingleton<ITechnicalDataTools, AShareTechnicalTools>(MarketType.AShare);
        services.AddKeyedSingleton<INewsDataTools, AShareNewsTools>(MarketType.AShare);
        services.AddKeyedSingleton<IShareSentimentTools, AShareSentimentTools>(MarketType.AShare);
        services.AddKeyedSingleton<ISentimentTools, AShareSentimentTools>(MarketType.AShare);

        // 注册 Agent Tools - 虚拟币实现（Keyed Services）
        services.AddKeyedSingleton<ICryptoBasicTools, CryptoBasicTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IBasicDataTools, CryptoBasicTools>(MarketType.Crypto); // 注册为基接口
        services.AddKeyedSingleton<ICryptoMetricsTools, CryptoMetricsTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IFinancialTools, CryptoMetricsTools>(MarketType.Crypto); // 注册为基接口
        services.AddKeyedSingleton<ITechnicalDataTools, CryptoTechnicalTools>(MarketType.Crypto);
        services.AddKeyedSingleton<INewsDataTools, CryptoNewsTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ICryptoSentimentTools, CryptoSentimentTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ISentimentTools, CryptoSentimentTools>(MarketType.Crypto);

        // 注册 Kernel 和嵌入服务（保留用于 RAG 和提示词模板）
        services.AddSingleton<IEmbeddingFactory, EmbeddingFactory>();

        // 注册 Agent Framework 服务
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IAnalystAgentFactory, AnalystAgentFactory>();

        // 注册 MCP 服务（Model Context Protocol）
        services.AddSingleton<McpService>();

        // 注册向量存储
        var store = Directory.GetCurrentDirectory() + "/vector.sqlite";
        services.AddSqliteVectorStore(_ => $"Data Source={store}");

        // 注册 RAG 和分析服务
        services.AddRagServices();
        services.AddSingleton<GroundingSearchTools>();

        // 注册快讯服务接口的实现（使用 Keyed Services）
        services.AddKeyedSingleton<ITelegramService, AShareTelegramService>("AShare");
        services.AddKeyedSingleton<ITelegramService, CryptoTelegramService>("Crypto");

        // 注册新闻更新服务（使用 Keyed Services）
        services.AddKeyedSingleton<INewsUpdateService>(
            "AShare",
            (sp, key) => new NewsUpdateService(
                sp.GetRequiredKeyedService<ITelegramService>("AShare"),
                sp.GetRequiredService<ILogger<NewsUpdateService>>()));

        services.AddKeyedSingleton<INewsUpdateService>(
            "Crypto",
            (sp, key) => new NewsUpdateService(
                sp.GetRequiredKeyedService<ITelegramService>("Crypto"),
                sp.GetRequiredService<ILogger<NewsUpdateService>>()));

        // 注册分析缓存服务
        services.AddSingleton<IAnalysisCacheService, AnalysisCacheService>();

        // 注册浏览器服务
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton<StockScreenerService>();

        // 注册资产服务抽象 - A股实现（Keyed Services）
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IHomeAssetService, AShareHomeService>(MarketType.AShare);
        services.AddKeyedSingleton<IFavoriteService, AShareFavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetHistoryService, AShareHistoryService>(MarketType.AShare);
        services.AddKeyedSingleton<IKLineService, AShareKLineService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetCacheService, AShareAssetCacheService>(MarketType.AShare);

        // 注册资产服务抽象 - 虚拟币实现（Keyed Services）
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);
        services.AddKeyedSingleton<IHomeAssetService, CryptoHomeService>(MarketType.Crypto);
        services.AddKeyedSingleton<IFavoriteService, CryptoFavoriteService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetHistoryService, CryptoHistoryService>(MarketType.Crypto);
        services.AddKeyedSingleton<IKLineService, CryptoKLineService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetCacheService, CryptoAssetCacheService>(MarketType.Crypto);

        // 注册AI选股相关服务（使用 Agent Framework Workflows）
        // 注册筛选服务接口的实现（使用 Keyed Services）
        services.AddKeyedSingleton<IAssetScreenerService, StockScreenerService>("AShare");
        services.AddKeyedSingleton<IAssetScreenerService, CryptoScreenerService>("Crypto");

        // 注册投资选择策略
        services.AddSingleton<ICriteriaGenerationStrategy<StockCriteria>, StockCriteriaGenerationStrategy>();
        services.AddSingleton<ICriteriaGenerationStrategy<CryptoCriteria>, CryptoCriteriaGenerationStrategy>();
        services.AddKeyedSingleton<IAssetDataFormatter, StockDataFormatter>("AShare");
        services.AddKeyedSingleton<IAssetDataFormatter, CryptoDataFormatter>("Crypto");

        // 注册投资选择工作流的 Executors（泛型 + 共用）
        services.AddSingleton<GenerateCriteriaExecutor<StockCriteria>>();
        services.AddSingleton<GenerateCriteriaExecutor<CryptoCriteria>>();
        services.AddSingleton<ScreenInvestmentTargetsExecutor>();
        services.AddSingleton<AnalyzeAssetsExecutor>();

        // 注册投资选择工作流和服务
        services.AddSingleton<InvestmentSelectionWorkflow>();
        services.AddSingleton<InvestmentSelectionService>();

        // 注册市场分析相关服务（使用 Agent Framework Workflows - 最佳实践）
        services.AddSingleton<AnalysisDispatcherExecutor>();
        services.AddSingleton<AnalysisAggregatorExecutor>();
        services.AddSingleton<CoordinatorExecutor>();
        services.AddSingleton<MarketAnalysisWorkflow>();

        // 注册 MarketAnalysis Workflow Executors 的 Logger
        // （通过 DI 自动注入，无需额外配置）

        // 注册版本更新服务
        services.AddSingleton<IReleaseService, GitHubReleaseService>();

        // 注册 Avalonia 平台特定服务
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IBrowserService, BrowserService>();
        services.AddSingleton<NavigationService>();

        // 注意：AI解析器已移除，分析师直接返回结构化 JSON

        return services;
    }

    /// <summary>
    /// 注册所有ViewModels
    /// </summary>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // 注册主窗口 ViewModel
        services.AddTransient<MainWindowViewModel>();

        // 注册主要页面 ViewModels
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<FavoritesPageViewModel>();
        services.AddTransient<AssetSelectionPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AboutPageViewModel>();
        services.AddTransient<MCPConfigPageViewModel>();
        services.AddTransient<AssetPageViewModel>();

        // 注册 Home 子 ViewModels
        services.AddTransient<HomeSearchViewModel>();
        services.AddTransient<HotAssetsViewModel>();
        services.AddTransient<RecentAssetsViewModel>();
        services.AddTransient<TelegraphNewsViewModel>();

        // 注册 AI 分析相关 ViewModels
        services.AddTransient<AgentAnalysisViewModel>();
        services.AddTransient<AnalysisReportViewModel>();
        services.AddTransient<ChatSidebarViewModel>();

        return services;
    }

    /// <summary>
    /// 配置 Serilog 日志服务
    /// </summary>
    public static ILoggingBuilder ConfigureLogging(this ILoggingBuilder logging, IUserSettingService userSettingService)
    {
        var logPath = userSettingService.CurrentSetting.LogPath;
        try { Directory.CreateDirectory(logPath); } catch { }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logPath, "log.txt"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_000_000,
                retainedFileCountLimit: 7)
            .CreateLogger();

        logging.ClearProviders();
        logging.AddSerilog(Log.Logger);

        logging.SetMinimumLevel(LogLevel.Information);

        return logging;
    }
}
