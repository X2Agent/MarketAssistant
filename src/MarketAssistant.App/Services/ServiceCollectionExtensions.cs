using MarketAssistant.Applications;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Applications.News;
using MarketAssistant.Infrastructure.AdaptiveCards;
using MarketAssistant.Infrastructure.AdaptiveCards.Parsers;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Home;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 注册所有业务服务（非 UI 层，来自 App.Services）
        services.AddBusinessServices();

        // 注册 Avalonia 平台特定服务
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<NavigationService>();

        // AdaptiveCard 解析器责任链（解析器实现位于本工程 Infrastructure/AdaptiveCards/Parsers）
        services.AddSingleton<IJsonToAdaptiveCardParser, CoordinatorCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, FinancialCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, FundamentalCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, SentimentCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, NewsCardParser>();
        services.AddSingleton<IJsonToAdaptiveCardParser, TechnicalCardParser>();

        // AdaptiveCard 转换器（依赖上方注册的解析器责任链）
        services.AddSingleton<AdaptiveCardConverter>();

        // 注册全局异常处理器（Singleton，由 DI 创建实例）
        services.AddSingleton<GlobalExceptionHandler>();

        // 应用级交易确认：订阅 TradeExecutor.ConfirmationRequested 并弹全局对话框，
        // 使 HITL 确认不依赖交易监控页存活（单例构造即接管订阅）
        services.AddSingleton<TradeConfirmationService>();

        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // 页面 ViewModel 工厂：导航项点击时才实例化页面（配合 MainWindowViewModel 去服务定位）
        services.AddSingleton<IPageViewModelFactory, PageViewModelFactory>();

        services.AddTransient<MainWindowViewModel>();

        services.AddTransient<HomePageViewModel>();
        services.AddTransient<FavoritesPageViewModel>();
        services.AddTransient<PriceAlertPageViewModel>();
        services.AddTransient<AssetSelectionPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AboutPageViewModel>();
        services.AddTransient<MCPConfigPageViewModel>();
        services.AddTransient<AssetPageViewModel>();

        services.AddTransient<HomeSearchViewModel>();
        services.AddTransient<HotAssetsViewModel>();
        services.AddTransient<RecentAssetsViewModel>();
        services.AddTransient<TelegraphNewsViewModel>();

        services.AddTransient<AgentAnalysisViewModel>();
        services.AddTransient<AnalysisReportViewModel>();
        services.AddTransient<ChatSidebarViewModel>();

        services.AddTransient<TradingPageViewModel>();
        services.AddTransient<StrategyConfigViewModel>();
        services.AddTransient<TradeMonitorViewModel>();
        services.AddTransient<BalanceDetailPageViewModel>();
        services.AddTransient<TradeHistoryViewModel>();
        services.AddTransient<ApiKeyConfigViewModel>();

        return services;
    }
}
