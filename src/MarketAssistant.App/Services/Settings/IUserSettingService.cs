using MarketAssistant.Applications.Settings;

namespace MarketAssistant.Services.Settings;

/// <summary>
/// 用户设置服务接口
/// </summary>
public interface IUserSettingService
{
    /// <summary>
    /// 当前用户设置
    /// </summary>
    UserSetting CurrentSetting { get; }

    /// <summary>
    /// 从存储中加载设置
    /// </summary>
    void LoadSettings();

    /// <summary>
    /// 保存设置到存储
    /// </summary>
    void SaveSettings();

    /// <summary>
    /// 更新设置并保存
    /// </summary>
    /// <param name="setting">新的用户设置</param>
    void UpdateSettings(UserSetting setting);

    /// <summary>
    /// 重置设置为默认值
    /// </summary>
    void ResetSettings();
}

