using CommunityToolkit.Maui;
using MarketAssistant.Agents;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Filtering;
using MarketAssistant.Infrastructure;
using MarketAssistant.Services;
using MarketAssistant.Vectors;
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
            var logPath = GetUserLogPath() ?? Path.Combine(FileSystem.Current.AppDataDirectory, "logs");
            Directory.CreateDirectory(logPath);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(logPath, "log.txt"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10000000,
                    retainedFileCountLimit: 7)
                .CreateLogger();

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

            // 配置 MAUI 使用 Serilog
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);

            // 注册HttpClient服务
            builder.Services.AddHttpClient();

            // 注册用户设置服务为单例
            builder.Services.AddSingleton<IUserSettingService, UserSettingService>();
            // Add filters with logging.
            builder.Services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
            builder.Services.AddSingleton<IPromptRenderFilter, PromptRenderLoggingFilter>();
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter, AutoFunctionInvocationLoggingFilter>();
            // 注册UserSpecificKernelProvider服务
            builder.Services.AddSingleton<IUserSemanticKernelService, UserSemanticKernelService>();
            // 注册嵌入服务
            builder.Services.AddSingleton<IUserEmbeddingService, UserEmbeddingService>();
            builder.Services.AddSingleton<IKernelPluginConfig, KernelPluginConfig>();

            // 注册Kernel服务，从用户设置中动态创建
            builder.Services.AddSingleton(serviceProvider =>
            {
                var userSemanticKernelService = serviceProvider.GetRequiredService<IUserSemanticKernelService>();
                var kernel = userSemanticKernelService.GetKernel();
                return kernel;
            });

            builder.Services.AddSingleton(serviceProvider =>
            {
                var userEmbeddingService = serviceProvider.GetRequiredService<IUserEmbeddingService>();
                var embeddingGenerator = userEmbeddingService.CreateEmbeddingGenerator();
                return embeddingGenerator;
            });

            var store = Directory.GetCurrentDirectory() + "/vector.sqlite";
            builder.Services.AddSqliteVectorStore(_ => $"Data Source={store}");

            // DataUploader 已废弃，摄取统一走 IRagIngestionService
            builder.Services.AddRagServices();
            builder.Services.AddSingleton<TelegramService>();
            builder.Services.AddSingleton<AnalystManager>();
            builder.Services.AddSingleton<MarketAnalysisAgent>();
            builder.Services.AddSingleton<StockService>();
            builder.Services.AddSingleton<StockKLineService>();
            builder.Services.AddSingleton<StockSearchHistory>();
            builder.Services.AddSingleton<StockFavoriteService>();
            builder.Services.AddSingleton<PlaywrightService>();

            // 注册AI选股相关服务
            builder.Services.AddSingleton<StockSelectionManager>();
            builder.Services.AddSingleton<StockSelectionService>();
            builder.Services.AddSingleton<IWindowsService, WindowsService>();
            builder.Services.AddSingleton<IApplicationExitService, ApplicationExitService>();

            builder.Services.AddSingleton<GitHubReleaseService>();

            // 注册AI解析器
            builder.Services.AddAnalystDataParsers();

            builder.Services.AddTransient<AnalysisReportViewModel>();

            // Register view models.
            builder.Services.AddTransient<HomeViewModel>();
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

        /// <summary>
        /// 获取用户设置的日志路径
        /// </summary>
        /// <returns>用户设置的日志路径，如果未设置则返回null</returns>
        private static string? GetUserLogPath()
        {
            try
            {
                // 尝试从Preferences中读取用户设置
                var settingsJson = Preferences.Default.Get("UserSettings", string.Empty);
                if (!string.IsNullOrEmpty(settingsJson))
                {
                    var userSetting = System.Text.Json.JsonSerializer.Deserialize<UserSetting>(settingsJson);
                    if (!string.IsNullOrEmpty(userSetting?.LogPath))
                    {
                        return userSetting.LogPath;
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果读取用户设置失败，记录错误但不影响应用启动
                System.Diagnostics.Debug.WriteLine($"读取用户日志路径设置失败: {ex.Message}");
            }

            return null;
        }
    }
}