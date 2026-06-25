using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Home;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.Logging;

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
        // 注册所有业务服务（非 UI 层，来自 App.Services）
        services.AddBusinessServices();

        // 注册 Avalonia 平台特定服务
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<NavigationService>();

        // 注册全局异常处理器（Singleton，由 DI 创建实例）
        services.AddSingleton<GlobalExceptionHandler>();

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

        // 注册交易模块 ViewModels
        services.AddTransient<TradingPageViewModel>();
        services.AddTransient<StrategyConfigViewModel>();
        services.AddTransient<TradeMonitorViewModel>();
        services.AddTransient<TradeHistoryViewModel>();

        return services;
    }
}
