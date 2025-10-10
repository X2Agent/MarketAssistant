using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase, IRecipient<NavigationMessage>
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly NavigationService _navigationService;

        [ObservableProperty]
        private ViewModelBase? _currentPage;

        [ObservableProperty]
        private NavigationItemViewModel? _selectedNavigationItem;

        [ObservableProperty]
        private bool _canGoBack;

        [ObservableProperty]
        private string _currentPageTitle = string.Empty;

        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

        public MainWindowViewModel(
            IServiceProvider serviceProvider,
            NavigationService navigationService,
            ILogger<MainWindowViewModel>? logger = null)
            : base(logger)
        {
            _serviceProvider = serviceProvider;
            _navigationService = navigationService;

            NavigationItems = new ObservableCollection<NavigationItemViewModel>
            {
                new NavigationItemViewModel("首页", "avares://MarketAssistant/Assets/Images/tab_home.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_home_on.svg", () => _serviceProvider.GetRequiredService<HomePageViewModel>()),
                new NavigationItemViewModel("收藏", "avares://MarketAssistant/Assets/Images/tab_favorites.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_favorites_on.svg", () => _serviceProvider.GetRequiredService<FavoritesPageViewModel>()),
                new NavigationItemViewModel("AI选股", "avares://MarketAssistant/Assets/Images/tab_analysis.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_analysis_on.svg", () => _serviceProvider.GetRequiredService<StockSelectionPageViewModel>()),
                new NavigationItemViewModel("设置", "avares://MarketAssistant/Assets/Images/tab_settings.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_settings_on.svg", () => _serviceProvider.GetRequiredService<SettingsPageViewModel>()),
                new NavigationItemViewModel("关于", "avares://MarketAssistant/Assets/Images/tab_about.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_about_on.svg", () => _serviceProvider.GetRequiredService<AboutPageViewModel>())
            };

            // 订阅导航服务事件
            _navigationService.Navigated += OnNavigated;
            _navigationService.CanGoBackChanged += OnCanGoBackChanged;

            // 默认导航到首页
            SelectedNavigationItem = NavigationItems[0];
            var homeViewModel = SelectedNavigationItem.CreateViewModel();
            _navigationService.NavigateToRoot(homeViewModel, SelectedNavigationItem.Title);

            // 注册导航消息监听
            WeakReferenceMessenger.Default.Register(this);
        }

        /// <summary>
        /// 返回命令
        /// </summary>
        [RelayCommand]
        private void GoBack()
        {
            _navigationService.GoBack();
        }

        private void OnNavigated(object? sender, NavigationItem navigationItem)
        {
            // 在UI线程更新当前页面
            Dispatcher.UIThread.Post(() =>
            {
                CurrentPage = navigationItem.ViewModel;

                // 更新页面标题
                CurrentPageTitle = GetPageTitle(navigationItem.ViewModel);

                // 如果是根导航项，更新左侧导航选择
                if (navigationItem.RootNavigationItemTitle != null)
                {
                    SelectedNavigationItem = NavigationItems.FirstOrDefault(
                        item => item.Title == navigationItem.RootNavigationItemTitle);
                }

                Logger?.LogDebug("导航完成: {PageType}, 导航栈深度: {Depth}",
                    navigationItem.ViewModel.GetType().Name, _navigationService.GetStackDepth());
            });
        }

        private void OnCanGoBackChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CanGoBack = _navigationService.CanGoBack;
            });
        }

        private string GetPageTitle(ViewModelBase viewModel)
        {
            return viewModel switch
            {
                StockPageViewModel => "股票详情",
                AgentAnalysisViewModel => "AI股票分析",
                MCPConfigPageViewModel => "MCP服务器配置",
                _ => string.Empty
            };
        }

        partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
        {
            if (value != null)
            {
                var viewModel = value.CreateViewModel();
                _navigationService.NavigateToRoot(viewModel, value.Title);
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
                    var mcpConfigViewModel = _serviceProvider.GetRequiredService<MCPConfigPageViewModel>();
                    _navigationService.NavigateTo(mcpConfigViewModel);
                    break;

                case "Stock":
                    var stockViewModel = _serviceProvider.GetRequiredService<StockPageViewModel>();
                    var stockParameter = message.Parameter;
                    _navigationService.NavigateTo(stockViewModel, stockParameter);

                    // 立即在UI线程异步加载股票数据
                    if (stockParameter is Dictionary<string, object> parameters &&
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
                    var analysisParameter = message.Parameter;
                    _navigationService.NavigateTo(analysisViewModel, analysisParameter);

                    // 立即在UI线程异步加载分析数据
                    if (analysisParameter is Dictionary<string, object> analysisParameters &&
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
