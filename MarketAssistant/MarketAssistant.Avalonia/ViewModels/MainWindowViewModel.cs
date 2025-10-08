using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace MarketAssistant.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase, IRecipient<NavigationMessage>
    {
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty]
        private ViewModelBase? _currentPage;

        [ObservableProperty]
        private NavigationItemViewModel? _selectedNavigationItem;

        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

        public MainWindowViewModel(
            IServiceProvider serviceProvider,
            ILogger<MainWindowViewModel>? logger = null) 
            : base(logger)
        {
            _serviceProvider = serviceProvider;
            
            NavigationItems = new ObservableCollection<NavigationItemViewModel>
            {
                new NavigationItemViewModel("首页", "avares://MarketAssistant.Avalonia/Assets/Images/tab_home.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_home_on.svg", () => _serviceProvider.GetRequiredService<HomePageViewModel>()),
                new NavigationItemViewModel("收藏", "avares://MarketAssistant.Avalonia/Assets/Images/tab_favorites.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_favorites_on.svg", () => _serviceProvider.GetRequiredService<FavoritesPageViewModel>()),
                new NavigationItemViewModel("AI选股", "avares://MarketAssistant.Avalonia/Assets/Images/tab_analysis.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_analysis_on.svg", () => _serviceProvider.GetRequiredService<StockSelectionPageViewModel>()),
                new NavigationItemViewModel("设置", "avares://MarketAssistant.Avalonia/Assets/Images/tab_settings.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_settings_on.svg", () => _serviceProvider.GetRequiredService<SettingsPageViewModel>()),
                new NavigationItemViewModel("关于", "avares://MarketAssistant.Avalonia/Assets/Images/tab_about.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_about_on.svg", () => _serviceProvider.GetRequiredService<AboutPageViewModel>())
            };

            // 默认选择首页
            SelectedNavigationItem = NavigationItems[0];
            CurrentPage = SelectedNavigationItem.CreateViewModel();

            // 注册导航消息监听
            WeakReferenceMessenger.Default.Register(this);
        }

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
    {
        if (value != null)
        {
            CurrentPage = value.CreateViewModel();
        }
    }

    partial void OnCurrentPageChanged(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        // 释放旧页面资源
        if (oldValue is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
                Logger?.LogDebug($"已释放 ViewModel 资源: {oldValue.GetType().Name}");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, $"释放 ViewModel 资源时发生错误: {oldValue.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// 接收导航消息
    /// </summary>
    public void Receive(NavigationMessage message)
    {
        switch (message.PageName)
        {
            case "MCPConfig":
                CurrentPage = _serviceProvider.GetRequiredService<MCPConfigPageViewModel>();
                SelectedNavigationItem = null; // 清除左侧导航选择
                break;
                
            case "Stock":
                var stockViewModel = _serviceProvider.GetRequiredService<StockPageViewModel>();
                // 先切换页面，让UI立即响应
                CurrentPage = stockViewModel;
                SelectedNavigationItem = null; // 清除左侧导航选择
                
                // 立即在UI线程异步加载股票数据
                if (message.Parameter is Dictionary<string, object> parameters && 
                    parameters.TryGetValue("code", out var code))
                {
                    var stockCode = code?.ToString() ?? string.Empty;
                    // 使用 Dispatcher 在UI线程的下一个空闲时刻执行，确保页面已渲染
                    Dispatcher.UIThread.Post(() => 
                        stockViewModel.SetStockCode(stockCode), 
                        DispatcherPriority.Background);
                }
                break;
                
            case "Analysis":
                var analysisViewModel = _serviceProvider.GetRequiredService<AgentAnalysisViewModel>();
                // 先切换页面，让UI立即响应
                CurrentPage = analysisViewModel;
                SelectedNavigationItem = null; // 清除左侧导航选择
                
                // 立即在UI线程异步加载分析数据
                if (message.Parameter is Dictionary<string, object> analysisParameters && 
                    analysisParameters.TryGetValue("code", out var analysisCode))
                {
                    var stockCode = analysisCode?.ToString() ?? string.Empty;
                    Logger?.LogInformation("导航到 AI 股票分析页面，股票代码: {Code}", stockCode);
                    // 使用 Dispatcher 在UI线程的下一个空闲时刻执行
                    Dispatcher.UIThread.Post(async () =>
                    {
                        analysisViewModel.StockCode = stockCode;
                        await analysisViewModel.LoadAnalysisDataAsync();
                    }, DispatcherPriority.Background);
                }
                else
                {
                    Logger?.LogInformation("导航到 AI 股票分析页面，但未提供股票代码");
                }
                break;
        }
    }
}

    public class NavigationItemViewModel : ViewModelBase
    {
        public string Title { get; }
        public string IconPath { get; }
        public string SelectedIconPath { get; }
        public Func<ViewModelBase> CreateViewModel { get; }

        public NavigationItemViewModel(string title, string iconPath, string selectedIconPath, Func<ViewModelBase> createViewModel)
        {
            Title = title;
            IconPath = iconPath;
            SelectedIconPath = selectedIconPath;
            CreateViewModel = createViewModel;
        }
    }
}
