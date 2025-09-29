using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace MarketAssistant.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ViewModelBase? _currentPage;

        [ObservableProperty]
        private NavigationItemViewModel? _selectedNavigationItem;

        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

        public MainWindowViewModel()
        {
            NavigationItems = new ObservableCollection<NavigationItemViewModel>
            {
                new NavigationItemViewModel("首页", "avares://MarketAssistant.Avalonia/Assets/Images/tab_home.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_home_on.svg", () => new HomePageViewModel()),
                new NavigationItemViewModel("收藏", "avares://MarketAssistant.Avalonia/Assets/Images/tab_favorites.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_favorites_on.svg", () => new FavoritesPageViewModel()),
                new NavigationItemViewModel("设置", "avares://MarketAssistant.Avalonia/Assets/Images/tab_settings.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_settings_on.svg", () => new SettingsPageViewModel()),
                new NavigationItemViewModel("关于", "avares://MarketAssistant.Avalonia/Assets/Images/tab_about.svg", "avares://MarketAssistant.Avalonia/Assets/Images/tab_about_on.svg", () => new AboutPageViewModel())
            };

            // 默认选择首页
            SelectedNavigationItem = NavigationItems[0];
            CurrentPage = SelectedNavigationItem.CreateViewModel();
        }

        partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
        {
            if (value != null)
            {
                CurrentPage = value.CreateViewModel();
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
