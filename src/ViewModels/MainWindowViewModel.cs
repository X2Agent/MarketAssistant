using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using MarketAssistant.ViewModels.Demo;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly NavigationService _navigationService;
        private readonly MarketContext _marketContext;
        private readonly INotificationService _notificationService;

        [ObservableProperty]
        private NavigationItemViewModel? _selectedNavigationItem;

        public ViewModelBase? CurrentPage => _navigationService.CurrentPage;
        public bool CanGoBack => _navigationService.CanGoBack;
        public string CurrentPageTitle => _navigationService.CurrentPage?.Title ?? string.Empty;

        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

        /// <summary>
        /// 当前市场类型显示文本
        /// </summary>
        public string CurrentMarketText => _marketContext.CurrentMarket == MarketType.AShare ? "A股市场" : "虚拟币市场";

        public MainWindowViewModel(
            IServiceProvider serviceProvider,
            NavigationService navigationService,
            MarketContext marketContext,
            INotificationService notificationService,
            ILogger<MainWindowViewModel>? logger = null)
            : base(logger)
        {
            _serviceProvider = serviceProvider;
            _navigationService = navigationService;
            _marketContext = marketContext;
            _notificationService = notificationService;

            NavigationItems = new ObservableCollection<NavigationItemViewModel>
            {
#if DEBUG
                new NavigationItemViewModel("Chat Demo", "avares://MarketAssistant/Assets/Images/tab_analysis.svg", "avares://MarketAssistant/Assets/Images/tab_analysis_on.svg", () => new ChatSidebarDemoViewModel()),
#endif
                new NavigationItemViewModel("首页", "avares://MarketAssistant/Assets/Images/tab_home.svg", "avares://MarketAssistant/Assets/Images/tab_home_on.svg", () => _serviceProvider.GetRequiredService<HomePageViewModel>()),
                new NavigationItemViewModel("收藏", "avares://MarketAssistant/Assets/Images/tab_favorites.svg", "avares://MarketAssistant/Assets/Images/tab_favorites_on.svg", () => _serviceProvider.GetRequiredService<FavoritesPageViewModel>()),
                new NavigationItemViewModel("AI选股", "avares://MarketAssistant/Assets/Images/tab_analysis.svg", "avares://MarketAssistant/Assets/Images/tab_analysis_on.svg", () => _serviceProvider.GetRequiredService<AssetSelectionPageViewModel>()),
                new NavigationItemViewModel("持仓", "avares://MarketAssistant/Assets/Images/tab_favorites.svg", "avares://MarketAssistant/Assets/Images/tab_favorites_on.svg", () => _serviceProvider.GetRequiredService<PortfolioPageViewModel>()),
                new NavigationItemViewModel("设置", "avares://MarketAssistant/Assets/Images/tab_settings.svg", "avares://MarketAssistant/Assets/Images/tab_settings_on.svg", () => _serviceProvider.GetRequiredService<SettingsPageViewModel>()),
                new NavigationItemViewModel("关于", "avares://MarketAssistant/Assets/Images/tab_about.svg", "avares://MarketAssistant/Assets/Images/tab_about_on.svg", () => _serviceProvider.GetRequiredService<AboutPageViewModel>())
            };

            // 监听导航服务属性变更
            _navigationService.PropertyChanged += OnNavigationServicePropertyChanged;

            // 监听市场切换事件
            _marketContext.PropertyChanged += OnMarketContextPropertyChanged;

            // 默认导航到首页
            SelectedNavigationItem = NavigationItems[0];
            var homeViewModel = SelectedNavigationItem.CreateViewModel();
            _navigationService.NavigateToRoot(homeViewModel, SelectedNavigationItem.Title);
        }

        private void OnMarketContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MarketContext.CurrentMarket))
            {
                OnPropertyChanged(nameof(CurrentMarketText));
            }
        }

        private void OnNavigationServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NavigationService.CurrentPage))
            {
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(CurrentPageTitle));
            }
            else if (e.PropertyName == nameof(NavigationService.CanGoBack))
            {
                OnPropertyChanged(nameof(CanGoBack));
            }
            else if (e.PropertyName == nameof(NavigationService.CurrentRootNavigationItemTitle))
            {
                if (_navigationService.CurrentRootNavigationItemTitle != null)
                {
                    SelectedNavigationItem = NavigationItems.FirstOrDefault(
                        item => item.Title == _navigationService.CurrentRootNavigationItemTitle);
                }
            }
        }

        /// <summary>
        /// 返回命令
        /// </summary>
        [RelayCommand]
        private void GoBack()
        {
            _navigationService.GoBack();
        }

        /// <summary>
        /// 切换市场命令（Logo点击或Ctrl+M快捷键）
        /// </summary>
        [RelayCommand]
        private void ToggleMarket()
        {
            // 切换市场类型
            var newMarket = _marketContext.CurrentMarket == MarketType.AShare 
                ? MarketType.Crypto 
                : MarketType.AShare;
            
            _marketContext.SwitchMarket(newMarket);

            // 显示切换提示
            var marketName = newMarket == MarketType.AShare ? "A股市场" : "虚拟币市场";
            _notificationService.ShowSuccess($"已切换到{marketName}");

            Logger?.LogInformation("市场已切换到: {Market} ({MarketName})", newMarket, marketName);

            // 刷新当前页面（重新加载数据）
            if (SelectedNavigationItem != null)
            {
                var currentTitle = SelectedNavigationItem.Title;
                var viewModel = SelectedNavigationItem.CreateViewModel();
                _navigationService.NavigateToRoot(viewModel, currentTitle);
            }
        }

        partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
        {
            if (value != null)
            {
                // 避免重复导航
                if (_navigationService.CurrentRootNavigationItemTitle == value.Title)
                {
                    return;
                }

                var viewModel = value.CreateViewModel();
                _navigationService.NavigateToRoot(viewModel, value.Title);
            }
        }
    }

    public class NavigationItemViewModel : ViewModelBase
    {
        public override string Title { get; }
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
