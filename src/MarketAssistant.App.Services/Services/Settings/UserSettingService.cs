using MarketAssistant.Applications.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketAssistant.Services.Settings;

/// <summary>
/// 用户设置服务，提供对UserSetting的统一访问和管理
/// </summary>
public class UserSettingService : IUserSettingService
{
    private readonly ILogger<UserSettingService> _logger;
    private readonly object _fileLock = new();
    private readonly string _settingsFilePath = Path.Combine(FileSystem.AppDataDirectory, AppInfo.UserSettingsFileName);

    private UserSetting _currentSetting = new();

    /// <summary>
    /// 当前用户设置
    /// </summary>
    public UserSetting CurrentSetting => _currentSetting;

    public UserSettingService(ILogger<UserSettingService>? logger = null)
    {
        _logger = logger ?? NullLogger<UserSettingService>.Instance;
        LoadSettings();
    }

    /// <summary>
    /// 从文件加载设置
    /// </summary>
    public void LoadSettings()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    _currentSetting = JsonSerializer.Deserialize<UserSetting>(json) ?? new UserSetting();
                }
                else
                {
                    _currentSetting = new UserSetting();
                }

                // 如果日志路径为空，设置为默认日志目录（与启动阶段保持一致）
                if (string.IsNullOrWhiteSpace(_currentSetting.LogPath))
                {
                    _currentSetting.LogPath = Path.Combine(FileSystem.AppDataDirectory, AppInfo.LogsDirectoryName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载设置时出错");

                // 如果加载失败，使用默认值
                _currentSetting = new UserSetting();
            }
        }
    }

    /// <summary>
    /// 保存设置到文件
    /// </summary>
    public void SaveSettings()
    {
        lock (_fileLock)
        {
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 序列化并保存
                var json = JsonSerializer.Serialize(_currentSetting, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存设置时出错");
            }
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

