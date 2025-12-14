using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Notification;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace MarketAssistant.ViewModels
{
    /// <summary>
    /// 关于页ViewModel
    /// </summary>
    public partial class AboutPageViewModel : ViewModelBase
    {
        private readonly IReleaseService _releaseService;
        private readonly INotificationService _notificationService;
        private ReleaseInfo? _latestRelease;

        [ObservableProperty]
        private bool _isCheckingUpdate;

        [ObservableProperty]
        private string _updateStatus = "点击检查更新";

        [ObservableProperty]
        private bool _hasNewVersion;

        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _latestVersion = "";

        [ObservableProperty]
        private string _releaseNotes = "";

        public string AppName => AppInfo.Title;
        public string Version => $"v {AppInfo.Version}";
        public string Description => AppInfo.Description;
        public string Company => AppInfo.Company;
        public string Copyright => AppInfo.Copyright;

        public ObservableCollection<FeatureItem> FeatureItems { get; } = new ObservableCollection<FeatureItem>();

        public IAsyncRelayCommand CheckUpdateCommand { get; }
        public IAsyncRelayCommand DownloadUpdateCommand { get; }
        public IRelayCommand OpenGitHubCommand { get; }

        /// <summary>
        /// 构造函数（使用依赖注入）
        /// </summary>
        public AboutPageViewModel(
            IReleaseService releaseService,
            INotificationService notificationService,
            ILogger<AboutPageViewModel> logger) : base(logger)
        {
            _releaseService = releaseService;
            _notificationService = notificationService;

            CheckUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync);
            DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync, () => HasNewVersion && !IsDownloading);
            OpenGitHubCommand = new RelayCommand(OpenGitHub);

            // 初始化功能项列表
            InitializeFeatureItems();
        }

        private async Task CheckForUpdateAsync()
        {
            await SafeExecuteAsync(async () =>
            {
                IsCheckingUpdate = true;
                UpdateStatus = "正在检查更新...";
                HasNewVersion = false;
                _latestRelease = null;

                try
                {
                    Logger?.LogInformation("开始检查更新，当前版本: {Version}", AppInfo.Version);

                    // 调用 IReleaseService 检查更新
                    var result = await _releaseService.CheckForUpdateAsync(AppInfo.Version, includePrerelease: true);

                    if (result.HasNewVersion && result.LatestRelease != null)
                    {
                        _latestRelease = result.LatestRelease;
                        LatestVersion = result.LatestRelease.TagName;
                        ReleaseNotes = result.LatestRelease.Body ?? "无更新说明";
                        HasNewVersion = true;
                        UpdateStatus = $"发现新版本：{result.LatestRelease.TagName}";

                        _notificationService.ShowInfo($"发现新版本 {result.LatestRelease.TagName}！\n点击下载按钮进行更新");
                        Logger?.LogInformation("发现新版本: {Version}", result.LatestRelease.TagName);

                        // 更新下载命令的可执行状态
                        DownloadUpdateCommand.NotifyCanExecuteChanged();
                    }
                    else
                    {
                        UpdateStatus = "已是最新版本 ✓";
                        _notificationService.ShowSuccess("当前已是最新版本！");
                        Logger?.LogInformation("当前已是最新版本");
                    }
                }
                catch (FriendlyException ex)
                {
                    UpdateStatus = $"检查更新失败：{ex.Message}";
                    _notificationService.ShowError($"检查更新失败：{ex.Message}");
                    Logger?.LogError(ex, "检查更新失败");
                }
                catch (Exception ex)
                {
                    UpdateStatus = "检查更新失败";
                    _notificationService.ShowError("检查更新失败，请稍后重试");
                    Logger?.LogError(ex, "检查更新时发生未知错误");
                }
                finally
                {
                    IsCheckingUpdate = false;
                }
            }, "检查更新");
        }

        private async Task DownloadUpdateAsync()
        {
            if (!HasNewVersion || _latestRelease == null) return;

            await SafeExecuteAsync(async () =>
            {
                IsDownloading = true;
                DownloadProgress = 0;

                try
                {
                    // 获取下载URL（优先 Windows 安装包）
                    var asset = _latestRelease.Assets
                        ?.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                            a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        ?? _latestRelease.Assets?.FirstOrDefault();

                    if (asset == null || string.IsNullOrEmpty(asset.DownloadUrl))
                    {
                        // 没有找到资产文件，打开 GitHub Release 页面
                        _notificationService.ShowInfo("将打开 GitHub Release 页面手动下载");
                        OpenUrl(_latestRelease.HtmlUrl);
                        return;
                    }

                    // 确定保存路径
                    var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    downloadsPath = Path.Combine(downloadsPath, "Downloads");
                    if (!Directory.Exists(downloadsPath))
                    {
                        downloadsPath = Path.GetTempPath();
                    }

                    var savePath = Path.Combine(downloadsPath, asset.Name);
                    Logger?.LogInformation("准备下载更新：{Url} -> {Path}", asset.DownloadUrl, savePath);

                    _notificationService.ShowInfo($"开始下载 {asset.Name}...");

                    // 创建进度报告器
                    var progress = new Progress<double>(p =>
                    {
                        DownloadProgress = p * 100;
                        UpdateStatus = $"下载中... {DownloadProgress:F0}%";
                    });

                    // 下载更新文件
                    var downloadedPath = await _releaseService.DownloadUpdateAsync(
                        asset.DownloadUrl,
                        savePath,
                        progress);

                    Logger?.LogInformation("更新文件下载完成: {Path}", downloadedPath);

                    // 下载完成
                    UpdateStatus = "下载完成！";
                    _notificationService.ShowSuccess($"更新文件已下载到：\n{downloadedPath}\n\n请手动运行安装程序进行更新");

                    // 打开下载目录
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = downloadsPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus = "下载已取消";
                    _notificationService.ShowWarning("下载已取消");
                    Logger?.LogWarning("下载已取消");
                }
                catch (FriendlyException ex)
                {
                    UpdateStatus = $"下载失败：{ex.Message}";
                    _notificationService.ShowError($"下载失败：{ex.Message}");
                    Logger?.LogError(ex, "下载更新失败");
                }
                catch (Exception ex)
                {
                    UpdateStatus = "下载失败";
                    _notificationService.ShowError("下载失败，请稍后重试或手动访问 GitHub 下载");
                    Logger?.LogError(ex, "下载更新时发生未知错误");
                }
                finally
                {
                    IsDownloading = false;
                    DownloadProgress = 0;
                    DownloadUpdateCommand.NotifyCanExecuteChanged();
                }
            }, "下载更新");
        }

        private void OpenGitHub()
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.GitHubRepoUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // 处理异常
            }
        }

        private void OpenUrl(string url)
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
                Command = new RelayCommand(() => OpenUrl(AppInfo.ChangelogUrl))
            });

            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/globe.svg",
                Title = "官方网站",
                ButtonText = "查看",
                Command = new RelayCommand(() => OpenUrl(AppInfo.OfficialWebsite))
            });

            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/feedback.svg",
                Title = "意见反馈",
                ButtonText = "反馈",
                Command = new RelayCommand(() => OpenUrl(AppInfo.FeedbackUrl))
            });

            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/license.svg",
                Title = "许可证",
                ButtonText = "查看",
                Command = new RelayCommand(() => OpenUrl(AppInfo.LicenseUrl))
            });

            FeatureItems.Add(new FeatureItem
            {
                IconSource = "/Assets/Images/qq.svg",
                Title = $"官方QQ群: {AppInfo.QQGroupNumber}",
                ButtonText = "加入",
                Command = new RelayCommand(() => OpenUrl(AppInfo.QQGroupUrl))
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
        public IRelayCommand Command { get; set; } = null!;
    }
}
