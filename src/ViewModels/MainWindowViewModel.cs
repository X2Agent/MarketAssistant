using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Services.Navigation;
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

        [ObservableProperty]
        private NavigationItemViewModel? _selectedNavigationItem;

        public ViewModelBase? CurrentPage => _navigationService.CurrentPage;
        public bool CanGoBack => _navigationService.CanGoBack;
        public string CurrentPageTitle => _navigationService.CurrentPage?.Title ?? string.Empty;

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
#if DEBUG
                new NavigationItemViewModel("Chat Demo", "avares://MarketAssistant/Assets/Images/tab_analysis.svg", "avares://MarketAssistant/Assets/Images/tab_analysis_on.svg", () => new ChatSidebarDemoViewModel()),
#endif
                new NavigationItemViewModel("首页", "avares://MarketAssistant/Assets/Images/tab_home.svg", "avares://MarketAssistant/Assets/Images/tab_home_on.svg", () => _serviceProvider.GetRequiredService<HomePageViewModel>()),
                new NavigationItemViewModel("收藏", "avares://MarketAssistant/Assets/Images/tab_favorites.svg", "avares://MarketAssistant/Assets/Images/tab_favorites_on.svg", () => _serviceProvider.GetRequiredService<FavoritesPageViewModel>()),
                new NavigationItemViewModel("AI选股", "avares://MarketAssistant/Assets/Images/tab_analysis.svg", "avares://MarketAssistant/Assets/Images/tab_analysis_on.svg", () => _serviceProvider.GetRequiredService<StockSelectionPageViewModel>()),
                new NavigationItemViewModel("设置", "avares://MarketAssistant/Assets/Images/tab_settings.svg", "avares://MarketAssistant/Assets/Images/tab_settings_on.svg", () => _serviceProvider.GetRequiredService<SettingsPageViewModel>()),
                new NavigationItemViewModel("关于", "avares://MarketAssistant/Assets/Images/tab_about.svg", "avares://MarketAssistant/Assets/Images/tab_about_on.svg", () => _serviceProvider.GetRequiredService<AboutPageViewModel>())
            };

            // 监听导航服务属性变更
            _navigationService.PropertyChanged += OnNavigationServicePropertyChanged;

            // 默认导航到首页
            SelectedNavigationItem = NavigationItems[0];
            var homeViewModel = SelectedNavigationItem.CreateViewModel();
            _navigationService.NavigateToRoot(homeViewModel, SelectedNavigationItem.Title);
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
