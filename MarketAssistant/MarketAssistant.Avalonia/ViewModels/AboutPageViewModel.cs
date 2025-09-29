using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace MarketAssistant.Avalonia.ViewModels
{
    /// <summary>
    /// 关于页ViewModel
    /// </summary>
    public partial class AboutPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _isCheckingUpdate;

        [ObservableProperty]
        private string _updateStatus = "点击检查更新";

        [ObservableProperty]
        private bool _hasNewVersion;

        public string AppName => "Market Assistant";
        public string Version => "v 1.0.0";
        public string Description => "AI大模型构建的股票分析助手";

        public ObservableCollection<FeatureItem> FeatureItems { get; } = new ObservableCollection<FeatureItem>();

        public IAsyncRelayCommand CheckUpdateCommand { get; }
        public IAsyncRelayCommand DownloadUpdateCommand { get; }
        public IAsyncRelayCommand OpenGitHubCommand { get; }

        public AboutPageViewModel()
        {
            CheckUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync);
            DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync);
            OpenGitHubCommand = new AsyncRelayCommand(OpenGitHubAsync);

            // 初始化功能项列表
            InitializeFeatureItems();
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                IsCheckingUpdate = true;
                UpdateStatus = "正在检查更新...";

                // 模拟检查更新过程
                await Task.Delay(2000);

                UpdateStatus = "已是最新版本";
                HasNewVersion = false;
            }
            catch (Exception)
            {
                UpdateStatus = "检查更新失败";
            }
            finally
            {
                IsCheckingUpdate = false;
            }
        }

        private async Task DownloadUpdateAsync()
        {
            if (!HasNewVersion) return;

            try
            {
                // 打开GitHub发布页面
                await OpenGitHubAsync();
            }
            catch (Exception)
            {
                // 处理异常
            }
        }

        private async Task OpenGitHubAsync()
        {
            try
            {
                var url = "https://github.com/yourusername/MarketAssistant";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // 处理异常
            }
        }

        private async Task OpenUrlAsync(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // 处理异常
            }
        }

        private void InitializeFeatureItems()
        {
            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/refresh.svg",
                Title = "更新日志",
                ButtonText = "查看",
                Command = new AsyncRelayCommand(() => OpenUrlAsync("https://github.com/yourusername/MarketAssistant/releases"))
            });

            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/globe.svg",
                Title = "官方网站",
                ButtonText = "查看",
                Command = new AsyncRelayCommand(() => OpenUrlAsync("https://github.com/yourusername/MarketAssistant"))
            });

            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/feedback.svg",
                Title = "意见反馈",
                ButtonText = "反馈",
                Command = new AsyncRelayCommand(() => OpenUrlAsync("https://github.com/yourusername/MarketAssistant/issues"))
            });

            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/license.svg",
                Title = "许可证",
                ButtonText = "查看",
                Command = new AsyncRelayCommand(() => OpenUrlAsync("https://github.com/yourusername/MarketAssistant/blob/main/LICENSE"))
            });
        }
    }

    public class FeatureItem
    {
        /// <summary>
        /// 功能项图标
        /// </summary>
        public string IconSource { get; set; } = "";

        /// <summary>
        /// 功能项名称
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// 按钮文本
        /// </summary>
        public string ButtonText { get; set; } = "";

        /// <summary>
        /// 功能项命令
        /// </summary>
        public IAsyncRelayCommand Command { get; set; } = null!;
    }
}
