using MarketAssistant.Applications.Settings;
using System.Text.Json;

namespace MarketAssistant.Infrastructure;

/// <summary>
/// 用户设置服务，提供对UserSetting的统一访问和管理
/// </summary>
public class UserSettingService : IUserSettingService
{
    private const string PreferenceKey = "UserSettings";
    private readonly IBrowserService? _browserService;

    private UserSetting _currentSetting = new();

    /// <summary>
    /// 当前用户设置
    /// </summary>
    public UserSetting CurrentSetting => _currentSetting;

    public UserSettingService(IBrowserService? browserService = null)
    {
        // 从存储中加载设置
        _browserService = browserService;
        LoadSettings();
    }

    /// <summary>
    /// 从存储中加载设置
    /// </summary>
    public void LoadSettings()
    {
        try
        {
            // 从Preferences加载设置
            string settingsJson = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (!string.IsNullOrEmpty(settingsJson))
            {
                _currentSetting = JsonSerializer.Deserialize<UserSetting>(settingsJson) ?? new UserSetting();
            }
            else
            {
                _currentSetting = new UserSetting();
            }

            // 如果日志路径为空，设置为默认日志目录（与启动阶段保持一致）
            if (string.IsNullOrWhiteSpace(_currentSetting.LogPath))
            {
                _currentSetting.LogPath = Path.Combine(FileSystem.Current.AppDataDirectory, "logs");
            }

            // 如果浏览器路径为空，则使用IBrowserService自动检测
            if (string.IsNullOrEmpty(_currentSetting.BrowserPath) && _browserService != null)
            {
                var (browserPath, found) = _browserService.CheckBrowser();
                if (found)
                {
                    _currentSetting.BrowserPath = browserPath;
                }
            }
        }
        catch (Exception ex)
        {
            // 处理异常
            System.Diagnostics.Debug.WriteLine($"加载设置时出错: {ex.Message}");
            Preferences.Default.Remove(PreferenceKey);

            // 如果加载失败，使用默认值
            _currentSetting = new UserSetting();
        }
    }

    /// <summary>
    /// 保存设置到存储
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            // 序列化设置对象
            string json = JsonSerializer.Serialize(_currentSetting);

            // 保存到Preferences
            Preferences.Default.Set(PreferenceKey, json);
        }
        catch (Exception ex)
        {
            // 处理异常
            System.Diagnostics.Debug.WriteLine($"保存设置时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 更新设置并保存
    /// </summary>
    /// <param name="setting">新的用户设置</param>
    public void UpdateSettings(UserSetting setting)
    {
        _currentSetting = setting;
        SaveSettings();
    }

    /// <summary>
    /// 重置设置为默认值
    /// </summary>
    public void ResetSettings()
    {
        _currentSetting = new UserSetting();
        SaveSettings();
    }
}