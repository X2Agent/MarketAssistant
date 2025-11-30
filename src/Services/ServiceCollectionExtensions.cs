using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.StockSelection;
using MarketAssistant.Agents.StockSelection.Executors;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Applications.News;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.StockSelection;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Extensions;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Cache;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.StockScreener;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Home;
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

        // 注册 Agent Tool 类
        services.AddSingleton<StockBasicTools>();
        services.AddSingleton<StockFinancialTools>();
        services.AddSingleton<StockTechnicalTools>();
        services.AddSingleton<GroundingSearchTools>();
        services.AddSingleton<StockNewsTools>();
        services.AddSingleton<MarketSentimentTools>();

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
        services.AddSingleton<TelegramService>();
        services.AddSingleton<GroundingSearchTools>();

        // 注册分析缓存服务
        services.AddSingleton<IAnalysisCacheService, AnalysisCacheService>();

        // 注册股票相关服务
        services.AddSingleton<StockService>();
        services.AddSingleton<StockKLineService>();
        services.AddSingleton<StockSearchHistory>();
        services.AddSingleton<StockFavoriteService>();
        services.AddSingleton<StockInfoCache>();
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton<StockScreenerService>();

        // 注册主页相关服务
        services.AddSingleton<IHomeStockService, HomeStockService>();
        services.AddSingleton<INewsUpdateService, NewsUpdateService>();

        // 注册AI选股相关服务（使用 Agent Framework Workflows）
        services.AddSingleton<GenerateCriteriaExecutor>();
        services.AddSingleton<ScreenStocksExecutor>();
        services.AddSingleton<AnalyzeStocksExecutor>();
        services.AddSingleton<StockSelectionWorkflow>();
        services.AddSingleton<StockSelectionService>();

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
        services.AddTransient<StockSelectionPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AboutPageViewModel>();
        services.AddTransient<MCPConfigPageViewModel>();
        services.AddTransient<StockPageViewModel>();

        // 注册 Home 子 ViewModels
        services.AddTransient<HomeSearchViewModel>();
        services.AddTransient<HotStocksViewModel>();
        services.AddTransient<RecentStocksViewModel>();
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

        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);

        return logging;
    }
}
