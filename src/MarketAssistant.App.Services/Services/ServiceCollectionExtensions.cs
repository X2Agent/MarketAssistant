using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Trading;
using MarketAssistant.Agents.InvestmentSelection;
using MarketAssistant.Applications.Analysis;
using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Crypto;
using MarketAssistant.Applications.Home;
using MarketAssistant.Applications.InvestmentSelection;
using MarketAssistant.Applications.News;
using MarketAssistant.Applications.PriceAlert;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Extensions;
using MarketAssistant.Services.Archive;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Cache;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Settings;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Exchanges;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MarketAssistant.Services;

/// <summary>
/// App.Services 业务服务注册扩展（非 UI 层）
/// </summary>
public static class BusinessServiceCollectionExtensions
{
    /// <summary>
    /// 注册所有业务服务（不含 UI/Avalonia 特定服务）
    /// </summary>
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClients();
        services.AddAgentTools();
        services.AddAgentInfrastructure();
        services.AddRagServices();
        services.AddMarketDataServices();
        services.AddTradingServices();
        services.AddMarketSpecificServices();
        services.AddWorkflowServices();
        services.AddSingleton<IReleaseService, GitHubReleaseService>();
        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 命名 HttpClient（内部）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient("Binance", client =>
        {
            client.BaseAddress = new Uri("https://api.binance.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler();

        services.AddHttpClient("BinanceFutures", client =>
        {
            client.BaseAddress = new Uri("https://fapi.binance.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler();

        services.AddHttpClient("CoinGecko", client =>
        {
            client.BaseAddress = new Uri("https://api.coingecko.com/api/v3");
            client.Timeout = TimeSpan.FromSeconds(25);
        }).AddStandardResilienceHandler();

        services.AddHttpClient("CoinDesk", client =>
        {
            client.BaseAddress = new Uri("https://data-api.coindesk.com");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }).AddStandardResilienceHandler();

        services.AddHttpClient("ZhiTu", client =>
        {
            client.BaseAddress = new Uri("https://api.zhituapi.com");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddStandardResilienceHandler();

        services.AddHttpClient("Cls", client =>
        {
            client.BaseAddress = new Uri("https://x-quote.cls.cn");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();

        services.AddHttpClient("GitHub", client =>
        {
            client.BaseAddress = new Uri(AppInfo.GitHubApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }).AddStandardResilienceHandler();

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Agent Tools — Keyed Services（A股 + 虚拟币）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddAgentTools(this IServiceCollection services)
    {
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<MarketContext>();
        services.AddKeyedSingleton<IMarketCapability, AShareMarketCapability>(MarketType.AShare);
        services.AddKeyedSingleton<IMarketCapability, CryptoMarketCapability>(MarketType.Crypto);

        // 通用工具
        services.AddSingleton<GroundingSearchTools>();

        // A股
        services.AddKeyedSingleton<IShareBasicTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<IBasicDataTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<IShareFinancialTools, AShareFinancialTools>(MarketType.AShare);
        services.AddKeyedSingleton<IFinancialTools, AShareFinancialTools>(MarketType.AShare);
        services.AddKeyedSingleton<ITechnicalDataTools, AShareTechnicalTools>(MarketType.AShare);
        services.AddKeyedSingleton<INewsDataTools, AShareNewsTools>(MarketType.AShare);
        services.AddKeyedSingleton<IShareSentimentTools, AShareSentimentTools>(MarketType.AShare);
        services.AddKeyedSingleton<ISentimentTools, AShareSentimentTools>(MarketType.AShare);

        // 虚拟币
        services.AddKeyedSingleton<ICryptoBasicTools, CryptoBasicTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IBasicDataTools, CryptoBasicTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ICryptoMetricsTools, CryptoMetricsTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IFinancialTools, CryptoMetricsTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ITechnicalDataTools, CryptoTechnicalTools>(MarketType.Crypto);
        services.AddKeyedSingleton<INewsDataTools, CryptoNewsTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ICryptoSentimentTools, CryptoSentimentTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ISentimentTools, CryptoSentimentTools>(MarketType.Crypto);

        // 交易工具（仅虚拟币）
        services.AddKeyedSingleton<ITradingExecutionTools, CryptoTradingExecutionTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IStrategyTools, CryptoStrategyTools>(MarketType.Crypto);

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Agent 基础设施（工厂 / MAF / MCP / 向量存储）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddAgentInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IEmbeddingFactory, EmbeddingFactory>();
        services.AddSingleton<IWebTextSearchFactory, WebTextSearchFactory>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IAnalystAgentFactory, AnalystAgentFactory>();
        services.AddSingleton<AnalystPromptLoader>();

        services.AddSingleton(sp =>
            new AgentSkillsProvider(
                skillPath: Path.Combine(AppContext.BaseDirectory, "skills")));

        services.AddSingleton<MCPServerConfigService>();
        services.AddSingleton<McpToolAuditLogger>();
        services.AddSingleton<McpService>();

        var store = Directory.GetCurrentDirectory() + "/vector.sqlite";
        services.AddSqliteVectorStore(_ => $"Data Source={store}");

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 市场行情 / 数据 API 服务
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddMarketDataServices(this IServiceCollection services)
    {
        services.AddSingleton<CoinGeckoApiService>();
        services.AddSingleton<BinanceMarketDataService>();
        services.AddSingleton<BinanceWebSocketService>();
        services.AddSingleton<PriceAlertService>();
        services.AddSingleton<ReportArchiveService>();
        services.AddSingleton<CoinDeskApiService>();
        services.AddSingleton<BinanceAuthService>();
        services.AddSingleton<BinanceAccountService>();
        services.AddSingleton<IAnalysisCacheService, AnalysisCacheService>();

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 自主交易模块
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddTradingServices(this IServiceCollection services)
    {
        services.AddSingleton<TradingDataService>();
        services.AddSingleton<RiskManager>();
        services.AddSingleton<StrategyEngine>();
        services.AddSingleton<TradeExecutor>();
        services.AddSingleton<MarketMonitor>();
        services.AddSingleton<CryptoPortfolioService>();
        services.AddSingleton<ITradingAgentFactory, TradingAgentFactory>();
        services.AddSingleton<IExchangeClient, BinanceExchangeClient>();

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 市场特定服务（Keyed：资产 / K线 / 缓存 / 快讯 / 新闻 / 筛选 / 浏览器）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddMarketSpecificServices(this IServiceCollection services)
    {
        // 快讯
        services.AddKeyedSingleton<ITelegramService, AShareTelegramService>(MarketType.AShare);
        services.AddKeyedSingleton<ITelegramService, CryptoTelegramService>(MarketType.Crypto);

        // 新闻更新
        services.AddKeyedSingleton<INewsUpdateService>(
            MarketType.AShare,
            (sp, key) => new NewsUpdateService(
                sp.GetRequiredKeyedService<ITelegramService>(MarketType.AShare),
                sp.GetRequiredService<ILogger<NewsUpdateService>>()));
        services.AddKeyedSingleton<INewsUpdateService>(
            MarketType.Crypto,
            (sp, key) => new NewsUpdateService(
                sp.GetRequiredKeyedService<ITelegramService>(MarketType.Crypto),
                sp.GetRequiredService<ILogger<NewsUpdateService>>()));

        // 浏览器自动化
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton<IBrowserService, BrowserService>();

        // A股资产服务
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IHomeAssetService, AShareHomeService>(MarketType.AShare);
        services.AddKeyedSingleton<IFavoriteService, AShareFavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetHistoryService, AShareHistoryService>(MarketType.AShare);
        services.AddKeyedSingleton<IKLineService, AShareKLineService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetCacheService, AShareAssetCacheService>(MarketType.AShare);

        // 虚拟币资产服务
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);
        services.AddKeyedSingleton<IHomeAssetService, CryptoHomeService>(MarketType.Crypto);
        services.AddKeyedSingleton<IFavoriteService, CryptoFavoriteService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetHistoryService, CryptoHistoryService>(MarketType.Crypto);
        services.AddKeyedSingleton<IKLineService, CryptoKLineService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetCacheService, CryptoAssetCacheService>(MarketType.Crypto);

        // 资产筛选（AI 选股）
        services.AddKeyedSingleton<IAssetScreenerService, StockScreenerService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetScreenerService, CryptoScreenerService>(MarketType.Crypto);

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 工作流服务（投资选择 + 市场分析）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        // 投资选择策略
        services.AddSingleton<ICriteriaGenerationStrategy<StockCriteria>, StockCriteriaGenerationStrategy>();
        services.AddSingleton<ICriteriaGenerationStrategy<CryptoCriteria>, CryptoCriteriaGenerationStrategy>();
        services.AddKeyedSingleton<IAssetDataFormatter, StockDataFormatter>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetDataFormatter, CryptoDataFormatter>(MarketType.Crypto);

        // 投资选择工作流
        services.AddSingleton<GenerateCriteriaExecutor<StockCriteria>>();
        services.AddSingleton<GenerateCriteriaExecutor<CryptoCriteria>>();
        services.AddSingleton<ScreenInvestmentTargetsExecutor>();
        services.AddSingleton<AnalyzeAssetsExecutor>();
        services.AddSingleton<InvestmentSelectionWorkflow>();
        services.AddSingleton<InvestmentSelectionService>();

        // 市场分析工作流
        services.AddSingleton<AnalysisDispatcherExecutor>();
        services.AddSingleton<AnalysisAggregatorExecutor>();
        services.AddSingleton<CoordinatorExecutor>();
        services.AddSingleton<MarketAnalysisWorkflow>();
        services.AddSingleton<AnalysisOrchestrationService>();

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
