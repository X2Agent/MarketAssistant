using CommunityToolkit.Mvvm.ComponentModel;

namespace MarketAssistant.Avalonia.ViewModels
{
    /// <summary>
    /// 关于页ViewModel
    /// </summary>
    public partial class AboutPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "关于";

        [ObservableProperty]
        private string _applicationName = "MarketAssistant";

        [ObservableProperty]
        private string _version = "1.0.0";

        [ObservableProperty]
        private string _description = "智能股票市场分析助手";

        [ObservableProperty]
        private string _copyright = "© 2024 MarketAssistant. All rights reserved.";

        public AboutPageViewModel()
        {
            // 可以在这里获取应用程序版本信息
        }
    }
}
