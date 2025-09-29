using CommunityToolkit.Mvvm.ComponentModel;

namespace MarketAssistant.Avalonia.ViewModels
{
    /// <summary>
    /// 首页ViewModel
    /// </summary>
    public partial class HomePageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "首页";

        [ObservableProperty]
        private string _welcomeMessage = "欢迎使用MarketAssistant";

        public HomePageViewModel()
        {
            // 可以在这里初始化首页相关的数据
        }
    }
}
