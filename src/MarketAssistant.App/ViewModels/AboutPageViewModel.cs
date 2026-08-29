using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Notification;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace MarketAssistant.ViewModels;

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

                // 当前版本为正式版时，不检查预发布版本，避免正式版用户被提示升级到 beta
                var includePrerelease = IsPrerelease(AppInfo.Version);
                var result = await _releaseService.CheckForUpdateAsync(AppInfo.Version, includePrerelease: includePrerelease);

                if (result.HasNewVersion && result.LatestRelease != null)
                {
                    _latestRelease = result.LatestRelease;
                    LatestVersion = result.LatestRelease.TagName;
                    ReleaseNotes = result.LatestRelease.Body ?? "无更新说明";
                    HasNewVersion = true;
                    UpdateStatus = $"发现新版本：{result.LatestRelease.TagName}";

                    _notificationService.ShowInfo($"发现新版本 {result.LatestRelease.TagName}！\n点击下载按钮进行更新");
                    Logger?.LogInformation("发现新版本: {Version}", result.LatestRelease.TagName);

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
                // 获取下载URL（优先匹配当前操作系统的安装包）
                var asset = SelectAssetForCurrentOs(_latestRelease)
                    ?? _latestRelease.Assets?.FirstOrDefault();

                if (asset == null || string.IsNullOrEmpty(asset.DownloadUrl))
                {
                    _notificationService.ShowInfo("将打开 GitHub Release 页面手动下载");
                    OpenUrl(_latestRelease.HtmlUrl);
                    return;
                }

                // 净化远端资产文件名：仅取文件名部分，并拒绝包含路径分隔符的名称，防止路径穿越
                var assetName = Path.GetFileName(asset.Name);
                if (string.IsNullOrEmpty(assetName) ||
                    asset.Name.Contains('/') || asset.Name.Contains('\\'))
                {
                    Logger?.LogWarning("检测到可疑的资产文件名，已拒绝下载: {Name}", asset.Name);
                    _notificationService.ShowError("更新资产文件名异常，已取消下载，请前往 GitHub 手动下载");
                    return;
                }

                var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                downloadsPath = Path.Combine(downloadsPath, "Downloads");
                if (!Directory.Exists(downloadsPath))
                {
                    downloadsPath = Path.GetTempPath();
                }

                var savePath = Path.Combine(downloadsPath, assetName);
                Logger?.LogInformation("准备下载更新：{Url} -> {Path}", asset.DownloadUrl, savePath);

                _notificationService.ShowInfo($"开始下载 {asset.Name}...");

                var progress = new Progress<double>(p =>
                {
                    DownloadProgress = p * 100;
                    UpdateStatus = $"下载中... {DownloadProgress:F0}%";
                });

                var downloadedPath = await _releaseService.DownloadUpdateAsync(
                    asset.DownloadUrl,
                    savePath,
                    progress);

                Logger?.LogInformation("更新文件下载完成: {Path}", downloadedPath);

                // 风险提示：下载产物未做哈希/签名校验（GitHub Release 未提供校验和信息），
                // 提示用户自行确认来源后再运行安装程序。
                UpdateStatus = "下载完成！";
                _notificationService.ShowSuccess($"更新文件已下载到：\n{downloadedPath}\n\n请确认文件来源后手动运行安装程序进行更新（当前未提供校验和验证）");

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
        catch (Exception ex)
        {
            // 打开浏览器失败不影响主流程，记录日志即可
            Logger?.LogWarning(ex, "打开 GitHub 仓库页面失败: {Url}", AppInfo.GitHubRepoUrl);
            _notificationService.ShowWarning("无法打开浏览器，请手动访问 GitHub 仓库");
        }
    }

    /// <summary>
    /// 根据当前操作系统选择合适的下载资产，避免向 macOS/Linux 用户推荐 Windows 安装包。
    /// 命名约定见 release.yml：Windows 优先 .exe/.msi，macOS 优先 .dmg，Linux 优先 .deb/.rpm。
    /// </summary>
    private static ReleaseAsset? SelectAssetForCurrentOs(ReleaseInfo release)
    {
        var assets = release.Assets;
        if (assets == null || assets.Count == 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(a => a.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase));
        }

        if (OperatingSystem.IsMacOS())
        {
            return assets.FirstOrDefault(a => a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(a => a.Name.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(a => a.Name.Contains("macOS", StringComparison.OrdinalIgnoreCase)
                                          || a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase)
                                          || a.Name.Contains("osx", StringComparison.OrdinalIgnoreCase));
        }

        if (OperatingSystem.IsLinux())
        {
            return assets.FirstOrDefault(a => a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(a => a.Name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(a => a.Name.Contains("Linux", StringComparison.OrdinalIgnoreCase)
                                          || a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>
    /// 判断版本号是否为预发布版本（包含 '-' 分隔符，如 1.0.0-beta1）。
    /// 正式版（如 1.0.0）不检查预发布更新，避免被提示升级到 beta。
    /// </summary>
    private static bool IsPrerelease(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var trimmed = version.TrimStart('v');
        return trimmed.Contains('-');
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // 打开链接失败不影响主流程，记录日志即可
            Logger?.LogWarning(ex, "打开链接失败: {Url}", url);
        }
    }

    private void InitializeFeatureItems()
    {
        FeatureItems.Add(new FeatureItem
        {
            IconSource = "/Assets/Images/refresh.svg",
            Title = "更新日志",
            Description = "了解每个版本的功能更新与问题修复",
            ButtonText = "查看",
            Command = new RelayCommand(() => OpenUrl(AppInfo.ChangelogUrl))
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "/Assets/Images/globe.svg",
            Title = "官方网站",
            Description = "访问项目主页，获取最新动态与文档",
            ButtonText = "查看",
            Command = new RelayCommand(() => OpenUrl(AppInfo.OfficialWebsite))
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "/Assets/Images/feedback.svg",
            Title = "意见反馈",
            Description = "提交遇到的问题或功能建议",
            ButtonText = "反馈",
            Command = new RelayCommand(() => OpenUrl(AppInfo.FeedbackUrl))
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "/Assets/Images/license.svg",
            Title = "许可证",
            Description = "查看本应用的开源许可条款",
            ButtonText = "查看",
            Command = new RelayCommand(() => OpenUrl(AppInfo.LicenseUrl))
        });

        FeatureItems.Add(new FeatureItem
        {
            IconSource = "/Assets/Images/qq.svg",
            Title = $"官方QQ群: {AppInfo.QQGroupNumber}",
            Description = "加入社区，与其他用户交流使用心得",
            ButtonText = "加入",
            Command = new RelayCommand(() => OpenUrl(AppInfo.QQGroupUrl))
        });
    }
}

public class FeatureItem
{
    public string IconSource { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string ButtonText { get; set; } = "";

    public IRelayCommand Command { get; set; } = null!;
}
