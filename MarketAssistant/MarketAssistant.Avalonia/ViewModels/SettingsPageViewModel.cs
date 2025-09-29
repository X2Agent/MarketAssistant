using CommunityToolkit.Mvvm.ComponentModel;

namespace MarketAssistant.Avalonia.ViewModels
{
    /// <summary>
    /// 设置页ViewModel
    /// </summary>
    public partial class SettingsPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "设置";

        [ObservableProperty]
        private bool _enableNotifications = true;

        [ObservableProperty]
        private bool _enableAutoRefresh = true;

        [ObservableProperty]
        private int _refreshInterval = 30;

        [ObservableProperty]
        private string _selectedTheme = "Light";

        public string[] AvailableThemes { get; } = { "Light", "Dark", "Auto" };

        public SettingsPageViewModel()
        {
            // 可以在这里加载用户设置
        }
    }
}
