using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace MarketAssistant.ViewModels;

public class AboutViewModel : ViewModelBase
{
    private readonly GitHubReleaseService _githubService;
    private readonly IUserSettingService _userSettingService;

    private bool _isCheckingUpdate;
    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        set => SetProperty(ref _isCheckingUpdate, value);
    }

    private string _updateStatus = "点击检查更新";
    public string UpdateStatus
    {
        get => _updateStatus;
        set => SetProperty(ref _updateStatus, value);
    }

    private bool _hasNewVersion;
    public bool HasNewVersion
    {
        get => _hasNewVersion;
        set => SetProperty(ref _hasNewVersion, value);
    }

    private ReleaseInfo? _latestRelease;
    public ReleaseInfo? LatestRelease
    {
        get => _latestRelease;
        set => SetProperty(ref _latestRelease, value);
    }

    public string AppName => ApplicationInfo.AppName;
    public string Version => ApplicationInfo.Version;
    public string Description => ApplicationInfo.Description;

    public IAsyncRelayCommand CheckUpdateCommand { get; }
    public IAsyncRelayCommand DownloadUpdateCommand { get; }
    public IAsyncRelayCommand OpenChangelogCommand { get; }
    public IAsyncRelayCommand OpenWebsiteCommand { get; }
    public IAsyncRelayCommand OpenFeedbackCommand { get; }
    public IAsyncRelayCommand OpenLicenseCommand { get; }

    public ObservableCollection<FeatureItem> FeatureItems { get; } = new ObservableCollection<FeatureItem>();

    public AboutViewModel(
        ILogger<AboutViewModel> logger,
        IUserSettingService userSettingService) : base(logger)
    {
        _githubService = new GitHubReleaseService();
        _userSettingService = userSettingService;

        CheckUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync);
        DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync);
        OpenChangelogCommand = new AsyncRelayCommand(() => OpenUrlAsync(ApplicationInfo.ChangelogUrl));
        OpenWebsiteCommand = new AsyncRelayCommand(() => OpenUrlAsync(ApplicationInfo.OfficialWebsite));
        OpenFeedbackCommand = new AsyncRelayCommand(() => OpenUrlAsync(ApplicationInfo.FeedbackUrl));
        OpenLicenseCommand = new AsyncRelayCommand(() => OpenUrlAsync(ApplicationInfo.LicenseUrl));

        // 初始化功能项列表
        InitializeFeatureItems();
    }

    private async Task CheckForUpdateAsync()
    {
        AppInfo.ShowSettingsUI();
        try
        {
            IsCheckingUpdate = true;
            UpdateStatus = "正在检查更新...";

            var (hasNewVersion, releaseInfo) = await _githubService.CheckForUpdateAsync(ApplicationInfo.Version);
            HasNewVersion = hasNewVersion;
            LatestRelease = releaseInfo;

            if (hasNewVersion && releaseInfo != null)
            {
                UpdateStatus = $"发现新版本: {releaseInfo.TagName}";
            }
            else
            {
                UpdateStatus = "已是最新版本";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败";
            Debug.WriteLine($"检查更新失败: {ex.Message}");
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task DownloadUpdateAsync()
    {
        if (LatestRelease == null || !HasNewVersion) return;

        try
        {
            var asset = LatestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip") || a.Name.EndsWith(".exe"));
            if (asset == null)
            {
                await OpenUrlAsync(LatestRelease.HtmlUrl);
                return;
            }

            // 打开下载页面
            await OpenUrlAsync(asset.DownloadUrl);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"下载更新失败: {ex.Message}");
        }
    }

    private void InitializeFeatureItems()
    {
        FeatureItems.Add(new FeatureItem
        {
            IconSource = "refresh.png",
            Title = "更新日志",
            ButtonText = "查看",
            Command = OpenChangelogCommand
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "globe.png",
            Title = "官方网站",
            ButtonText = "查看",
            Command = OpenWebsiteCommand
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "feedback.png",
            Title = "意见反馈",
            ButtonText = "反馈",
            Command = OpenFeedbackCommand
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "license.png",
            Title = "许可证",
            ButtonText = "查看",
            Command = OpenLicenseCommand
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "email.png",
            Title = "邮件联系",
            ButtonText = "邮件",
            Command = new AsyncRelayCommand(() => Launcher.OpenAsync("mailto:support@example.com"))
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
    public IAsyncRelayCommand Command { get; set; }
}