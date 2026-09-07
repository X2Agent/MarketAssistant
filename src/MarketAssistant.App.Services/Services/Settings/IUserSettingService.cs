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
    /// 在与持久化相同的同步边界内变更设置并保存。
    /// 跨线程修改设置（如交易线程切换环境、市场上下文切换市场）必须走此入口，
    /// 避免锁外变更与 <see cref="SaveSettings"/> 的并发序列化产生撕裂状态；
    /// UI 双向绑定在 UI 线程上的直接属性变更可继续使用 <see cref="CurrentSetting"/> + <see cref="SaveSettings"/>。
    /// </summary>
    void UpdateSetting(Action<UserSetting> mutate);

    /// <summary>
    /// 重置设置为默认值
    /// </summary>
    void ResetSettings();
}

