using MarketAssistant.Agents.ContextProviders;
using MarketAssistant.Agents.Middleware;
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
using MarketAssistant.Infrastructure.AdaptiveCards.Parsers;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Http;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Rag.Extensions;
using MarketAssistant.Services.Archive;
using MarketAssistant.Services.Cache;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using MarketAssistant.Services.Trading.Exchanges;
using System.Net;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Polly.RateLimiting;
using Serilog;
using System.Threading.RateLimiting;

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
        services.AddNamedMarketHttpClients();
        services.AddAgentTools();
        services.AddAgentInfrastructure();
        services.AddRagServices();
        services.AddMarketDataServices();
        services.AddTradingServices();
        services.AddWorkflowServices();
        services.AddMarketModules();
        services.AddSingleton<IReleaseService, GitHubReleaseService>();
        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 命名 HttpClient（公开别名便于单元测试与外部宿主复用）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 注册 Binance / CoinGecko / Cls 等命名 HttpClient，含标准弹性策略。
    /// </summary>
    public static IServiceCollection AddNamedMarketHttpClients(this IServiceCollection services) =>
        AddHttpClientsCore(services);

    internal static IServiceCollection AddHttpClients(this IServiceCollection services) =>
        AddHttpClientsCore(services);

    private static IServiceCollection AddHttpClientsCore(IServiceCollection services)
    {
        services.AddHttpClient("Binance", client =>
        {
            client.BaseAddress = new Uri("https://api.binance.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler(options =>
        {
            // 币安 REST API 限流：IP 维度 6000 weight/min，单连接并发过高易触发 429。
            // 桌面端场景下限制并发请求数即可，配合标准重试/熔断策略。
            ConfigureBinanceRateLimiter(options);
        });

        services.AddHttpClient("BinanceFutures", client =>
        {
            client.BaseAddress = new Uri("https://fapi.binance.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler(options =>
        {
            ConfigureBinanceRateLimiter(options);
        });

        // 合约 Testnet（demo-fapi.binance.com）—— 与现货 Testnet 完全独立，需单独 API Key
        services.AddHttpClient("BinanceFuturesTestnet", client =>
        {
            client.BaseAddress = new Uri("https://demo-fapi.binance.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler(options =>
        {
            ConfigureBinanceRateLimiter(options);
        });

        // 现货 Demo（demo-api.binance.com）—— 使用实盘账户的虚拟余额，需在 binance.com 申请 Demo API Key
        services.AddHttpClient("BinanceSpotDemo", client =>
        {
            client.BaseAddress = new Uri("https://demo-api.binance.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler(options =>
        {
            ConfigureBinanceRateLimiter(options);
        });

        // CoinGecko API Key 通过 DelegatingHandler 注入，避免 DataProviders 反向依赖 App.Services
        services.AddTransient<CoinGeckoApiKeyHandler>();

        services.AddHttpClient("CoinGecko", client =>
        {
            client.BaseAddress = new Uri("https://api.coingecko.com/api/v3/");
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MarketAssistant/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        })
        .AddHttpMessageHandler<CoinGeckoApiKeyHandler>()
        .AddStandardResilienceHandler();

        services.AddHttpClient("ZhiTu", client =>
        {
            client.BaseAddress = new Uri("https://api.zhituapi.com");
            client.Timeout = TimeSpan.FromSeconds(60);
        }).AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient("Cls", client =>
        {
            client.BaseAddress = new Uri("https://x-quote.cls.cn");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();

        // 东方财富搜索接口（新闻搜索等，返回 JSONP）
        services.AddHttpClient("EastMoneySearch", client =>
        {
            client.BaseAddress = new Uri("https://search-api-web.eastmoney.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();

        // 新浪财经资金流接口（热门股票资金流排行）
        // 原 push2.eastmoney.com 端点在部分网络环境下 TLS 重协商被中断，改用新浪接口
        services.AddHttpClient("SinaFinance", client =>
        {
            client.BaseAddress = new Uri("https://vip.stock.finance.sina.com.cn");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Referrer = new Uri("https://vip.stock.finance.sina.com.cn/");
        }).AddStandardResilienceHandler();

        // 雪球选股 API（支持全部 38 个筛选指标，Cookie 跨请求共享）
        services.AddSingleton<CookieContainer>();
        services.AddHttpClient("Xueqiu", client =>
        {
            client.BaseAddress = new Uri("https://xueqiu.com");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        })
        .ConfigurePrimaryHttpMessageHandler(sp => new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = sp.GetRequiredService<CookieContainer>()
        })
        .AddStandardResilienceHandler();

        // 虚拟币新闻 RSS 源（CoinTelegraph 等）
        services.AddHttpClient("CryptoNewsRss", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }).AddStandardResilienceHandler();

        // 同花顺快讯接口
        services.AddHttpClient("AShareTelegram", client =>
        {
            client.BaseAddress = new Uri("https://news.10jqka.com.cn");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();

        // 虚拟币快讯接口（PANews）
        services.AddHttpClient("CryptoTelegram", client =>
        {
            client.BaseAddress = new Uri("https://universal-api.panewslab.com");
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

        // 专用于下载 Release 二进制文件的 HttpClient，不带 GitHub API 专用 Accept 头
        services.AddHttpClient("GitHubDownload", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        });

        services.AddHttpClient("ModelDiscovery", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        }).AddStandardResilienceHandler();

        return services;
    }

    /// <summary>
    /// 为币安 HttpClient 配置并发限流：最多 10 个并发请求，排队上限 50。
    /// 币安 REST API 限流为 IP 维度 6000 weight/min，桌面端控制并发即可避免触发 429。
    /// </summary>
    private static void ConfigureBinanceRateLimiter(HttpStandardResilienceOptions options)
    {
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 50,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        options.RateLimiter = new HttpRateLimiterStrategyOptions
        {
            RateLimiter = args => limiter.AcquireAsync(1, args.Context.CancellationToken)
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Agent Tools — Keyed Services（A股 + 虚拟币）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddAgentTools(this IServiceCollection services)
    {
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<MarketContext>();

        // 通用工具
        services.AddSingleton<GroundingSearchTools>();
        services.AddSingleton<MemoryManagementTools>();
        services.AddSingleton<SessionSearchTools>();
        services.AddSingleton<KnowledgeGraphTools>();

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Agent 基础设施（工厂 / MAF / MCP / 向量存储）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddAgentInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IModelProviderAdapterFactory, ModelProviderAdapterFactory>();
        services.AddSingleton<IEmbeddingFactory, EmbeddingFactory>();
        // 延迟工厂：仅在向量化等真实场景解析，避免浏览设置页时构造嵌入/向量存储链路
        services.AddSingleton<Func<IEmbeddingFactory>>(sp => sp.GetRequiredService<IEmbeddingFactory>);
        services.AddSingleton<IWebSearchService, WebSearchService>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IAnalystAgentFactory, AnalystAgentFactory>();
        services.AddSingleton<AnalystPromptLoader>();

        // AdaptiveCard Parsers（责任链）
        services.AddSingleton<IJsonToAdaptiveCardParser, CoordinatorCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, FinancialCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, FundamentalCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, SentimentCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, NewsCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, TechnicalCardParser>();

        // MAF 中间件与会话级 Context Provider 工厂
        services.AddSingleton<TokenTrackingMiddleware>();
        services.AddSingleton<ConversationCompactionProviderFactory>();

        services.AddSingleton(sp =>
            new AgentSkillsProvider(
                skillPath: Path.Combine(AppContext.BaseDirectory, "skills")));

        services.AddSingleton<MCPServerConfigService>();
        services.AddSingleton<McpToolAuditLogger>();
        services.AddSingleton<McpService>();
        services.AddSingleton<McpToolContextProvider>();

        // AI Context Providers（Memory / RAG）
        services.AddSingleton<UserMemoryService>();
        services.AddSingleton<LayeredMemoryContextProvider>();
        services.AddSingleton<UserKnowledgeGraphService>();
        services.AddSingleton<MemoryExtractionService>();
        services.AddSingleton<ChatSessionPersistenceService>();
        services.AddSingleton<WorkflowVisualizationService>();
        services.AddSingleton<IMarketChatSessionFactory, MarketChatSessionFactory>();

        var store = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName,
            "vector.sqlite");
        services.AddSqliteVectorStore(_ => $"Data Source={store}");
        services.AddSingleton<Func<VectorStore>>(sp => sp.GetRequiredService<VectorStore>);

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 市场行情 / 数据 API 服务
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddMarketDataServices(this IServiceCollection services)
    {
        services.AddSingleton<CoinGeckoApiService>();
        services.AddSingleton<ICryptoAliasRegistry, CryptoAliasRegistry>();
        services.AddSingleton<BinanceMarketDataService>();
        services.AddSingleton<BinanceWebSocketService>();
        services.AddSingleton<BinanceUserDataStreamService>();
        services.AddSingleton<PriceAlertService>();
        services.AddSingleton<ReportArchiveService>();
        services.AddSingleton<IAnalysisCacheService, AnalysisCacheService>();

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 自主交易模块
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddTradingServices(this IServiceCollection services)
    {
        services.AddSingleton<ITradingCredentialStore, TradingCredentialStore>();
        services.AddSingleton<AnalysisReportCache>();
        services.AddSingleton<TradingDataService>();
        services.AddSingleton<TradingEnvironmentService>();
        // 打破 TradingEnvironmentService → MarketMonitor → BinanceUserDataStreamService → TradingEnvironmentService 的循环依赖
        services.AddSingleton<Func<MarketMonitor>>(sp => () => sp.GetRequiredService<MarketMonitor>());
        services.AddSingleton<RoutingExchangeClient>(CreateRoutingExchangeClient);
        services.AddSingleton<TradingStrategyService>();
        services.AddSingleton<RiskManager>();
        services.AddSingleton<StrategyEngine>();
        services.AddSingleton<AISignalStrategyExecutor>();
        services.AddSingleton<OrderStateSyncService>();
        services.AddSingleton<TradeExecutor>();
        services.AddSingleton<MarketMonitor>();
        services.AddSingleton<CryptoPortfolioService>();
        services.AddSingleton<ITradingAgentFactory, TradingAgentFactory>();

        return services;
    }

    /// <summary>
    /// 创建 RoutingExchangeClient，通过 ITradingCredentialStore 加密读取密钥，
    /// 为每种交易模式构建独立的鉴权/账户/客户端实例，注册到字典中路由。
    /// 支持现货实盘、现货 Demo、合约实盘、合约 Testnet 共 4 种模式。
    /// 实盘合约复用现货实盘 API Key（同一账户，需在 binance.com 开启合约权限）。
    /// </summary>
    private static RoutingExchangeClient CreateRoutingExchangeClient(IServiceProvider sp)
    {
        var env = sp.GetRequiredService<TradingEnvironmentService>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var credentialStore = sp.GetRequiredService<ITradingCredentialStore>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        var spotLogger = sp.GetRequiredService<ILogger<BinanceSpotAccountService>>();
        var futuresLogger = sp.GetRequiredService<ILogger<BinanceFuturesAccountService>>();

        // 为每种交易模式创建独立的鉴权服务（从加密存储读取密钥）
        var spotLiveAuth = new BinanceAuthService(credentialStore, CryptoTradingMode.LiveSpot,
            "Binance", httpClientFactory, "Binance", "/api/v3/time",
            loggerFactory.CreateLogger<BinanceAuthService>());
        var spotDemoAuth = new BinanceAuthService(credentialStore, CryptoTradingMode.BinanceSpotDemo,
            "Binance Spot Demo", httpClientFactory, "BinanceSpotDemo", "/api/v3/time",
            loggerFactory.CreateLogger<BinanceAuthService>());
        var futuresLiveAuth = new BinanceAuthService(credentialStore, CryptoTradingMode.LiveFutures,
            "Binance Futures", httpClientFactory, "BinanceFutures", "/fapi/v1/time",
            loggerFactory.CreateLogger<BinanceAuthService>());
        var futuresTestnetAuth = new BinanceAuthService(credentialStore, CryptoTradingMode.BinanceFuturesTestnet,
            "Binance Futures Testnet", httpClientFactory, "BinanceFuturesTestnet", "/fapi/v1/time",
            loggerFactory.CreateLogger<BinanceAuthService>());

        // 账户服务（HttpClient 名与标签不同）
        var spotLiveAccount = new BinanceSpotAccountService(httpClientFactory, spotLogger, spotLiveAuth, "Binance", "");
        var spotDemoAccount = new BinanceSpotAccountService(httpClientFactory, spotLogger, spotDemoAuth, "BinanceSpotDemo", "Demo ");
        var futuresLiveAccount = new BinanceFuturesAccountService(httpClientFactory, futuresLogger, futuresLiveAuth, "BinanceFutures", "");
        var futuresTestnetAccount = new BinanceFuturesAccountService(httpClientFactory, futuresLogger, futuresTestnetAuth, "BinanceFuturesTestnet", "Testnet ");

        // 交易所客户端
        var clients = new Dictionary<CryptoTradingMode, IExchangeClient>
        {
            [CryptoTradingMode.LiveSpot] = new BinanceExchangeClient(spotLiveAccount, "Binance"),
            [CryptoTradingMode.BinanceSpotDemo] = new BinanceExchangeClient(spotDemoAccount, "Binance Spot Demo"),
            [CryptoTradingMode.LiveFutures] = new BinanceFuturesExchangeClient(futuresLiveAccount, "Binance Futures"),
            [CryptoTradingMode.BinanceFuturesTestnet] = new BinanceFuturesExchangeClient(futuresTestnetAccount, "Binance Futures Testnet"),
        };

        return new RoutingExchangeClient(env, clients);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 工作流服务（投资选择 + 市场分析）
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        // 投资选择工作流
        services.AddSingleton<ScreenInvestmentTargetsExecutor>();
        services.AddSingleton<AnalyzeAssetsExecutor>();
        services.AddSingleton<InvestmentSelectionWorkflow>();
        services.AddSingleton<InvestmentSelectionService>();

        // 市场分析工作流；Executor 在每次 Run 内创建，避免共享可变状态和模型固化。
        services.AddSingleton<MarketAnalysisWorkflow>();
        services.AddSingleton<AnalysisOrchestrationService>();

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 市场模块——每个市场一个模块类，新增市场只需实现 IMarketModule 并加入列表
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly IMarketModule[] _marketModules =
    [
        new AShareMarketModule(),
        new CryptoMarketModule(),
    ];

    private static IServiceCollection AddMarketModules(this IServiceCollection services)
    {
        foreach (var module in _marketModules)
            module.Register(services);
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
