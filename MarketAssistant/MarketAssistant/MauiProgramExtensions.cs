using CommunityToolkit.Maui;
using MarketAssistant.Agents;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Filtering;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using MarketAssistant.Services;
using MarketAssistant.Vectors.Extensions;
using MarketAssistant.ViewModels;
using MarketAssistant.Views.Parsers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Serilog;

namespace MarketAssistant
{
    public static class MauiProgramExtensions
    {
        public static MauiAppBuilder UseSharedMauiApp(this MauiAppBuilder builder)
        {
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit(options =>
                {
                    options.SetShouldEnableSnackbarOnWindows(true);
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // 注册HttpClient服务
            builder.Services.AddHttpClient();

            // 注册内存缓存服务
            builder.Services.AddMemoryCache(options =>
            {
                options.SizeLimit = 50 * 1024 * 1024; // 50MB 限制
            });

            // 注册用户设置服务为单例
            builder.Services.AddSingleton<IUserSettingService, UserSettingService>();

            // 构建一个临时 ServiceProvider 以便在真正 Build 之前初始化日志（避免重复默认路径逻辑）
            using (var tempProvider = builder.Services.BuildServiceProvider())
            {
                var userSettingService = tempProvider.GetRequiredService<IUserSettingService>();
                // UserSettingService.LoadSettings 已保证为空时写入默认值
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

                builder.Logging.ClearProviders();
                builder.Logging.AddSerilog(Log.Logger);
            }

            // Add filters with logging.
            builder.Services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
            builder.Services.AddSingleton<IPromptRenderFilter, PromptRenderLoggingFilter>();
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter, AutoFunctionInvocationLoggingFilter>();
            // Prompt semantic cache (read) and write-back filter
            builder.Services.AddSingleton<IPromptRenderFilter, PromptCacheFilter>();
            builder.Services.AddSingleton<IFunctionInvocationFilter, PromptCacheWriteFilter>();
            // 注册用户 Kernel 服务（可失效重建）
            builder.Services.AddSingleton<IKernelFactory, KernelFactory>();
            // 注册嵌入服务
            builder.Services.AddSingleton<IEmbeddingFactory, EmbeddingFactory>();
            builder.Services.AddSingleton<IKernelPluginConfig, KernelPluginConfig>();

            // 提供 Kernel 访问。不要在启动阶段构造，首次需要时再创建。
            builder.Services.AddSingleton(serviceProvider =>
            {
                var svc = serviceProvider.GetRequiredService<IKernelFactory>();
                return svc.CreateKernel();
            });

            builder.Services.AddSingleton(serviceProvider =>
            {
                var embeddingFactory = serviceProvider.GetRequiredService<IEmbeddingFactory>();
                var embeddingGenerator = embeddingFactory.Create();
                return embeddingGenerator;
            });

            var store = Directory.GetCurrentDirectory() + "/vector.sqlite";
            builder.Services.AddSqliteVectorStore(_ => $"Data Source={store}");

            // DataUploader 已废弃，摄取统一走 IRagIngestionService
            builder.Services.AddRagServices();
            builder.Services.AddSingleton<TelegramService>();
            builder.Services.AddSingleton<GroundingSearchPlugin>();
            builder.Services.AddSingleton<AnalystManager>();
            builder.Services.AddSingleton<MarketAnalysisAgent>();

            // 注册分析缓存服务
            builder.Services.AddSingleton<IAnalysisCacheService, AnalysisCacheService>();

            builder.Services.AddSingleton<StockService>();
            builder.Services.AddSingleton<StockKLineService>();
            builder.Services.AddSingleton<StockSearchHistory>();
            builder.Services.AddSingleton<StockFavoriteService>();
            builder.Services.AddSingleton<PlaywrightService>();

            // 注册新的主页相关服务
            builder.Services.AddSingleton<IHomeStockService, HomeStockService>();
            builder.Services.AddSingleton<INewsUpdateService, NewsUpdateService>();

            // 注册AI选股相关服务
            builder.Services.AddSingleton<StockSelectionManager>();
            builder.Services.AddSingleton<StockSelectionService>();
            builder.Services.AddSingleton<IApplicationExitService, ApplicationExitService>();

            builder.Services.AddSingleton<GitHubReleaseService>();

            // 注册AI解析器
            builder.Services.AddAnalystDataParsers();

            builder.Services.AddTransient<AnalysisReportViewModel>();

            // Register view models.
            builder.Services.AddTransient<HomeViewModel>();
            
            // Register home sub-viewmodels
            builder.Services.AddTransient<MarketAssistant.ViewModels.Home.HomeSearchViewModel>();
            builder.Services.AddTransient<MarketAssistant.ViewModels.Home.HotStocksViewModel>();
            builder.Services.AddTransient<MarketAssistant.ViewModels.Home.RecentStocksViewModel>();
            builder.Services.AddTransient<MarketAssistant.ViewModels.Home.TelegraphNewsViewModel>();
            
            builder.Services.AddTransient<AgentAnalysisViewModel>();
            builder.Services.AddTransient<SettingViewModel>();
            builder.Services.AddTransient<StockViewModel>();
            builder.Services.AddTransient<MCPServerConfigViewModel>();
            builder.Services.AddTransient<FavoritesViewModel>();
            builder.Services.AddTransient<AboutViewModel>();
            builder.Services.AddTransient<StockSelectionViewModel>();

            builder.Services.AddSingleton<GlobalExceptionHandler>();
            builder.Services.AddSingleton<IApplicationExitService, ApplicationExitService>();

            return builder;
        }
    }
}