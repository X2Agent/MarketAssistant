using System.Collections.Concurrent;
using MarketAssistant.Applications.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketAssistant.Services.Settings;

/// <summary>
/// 用户设置服务，提供对UserSetting的统一访问和管理
/// </summary>
public class UserSettingService : IUserSettingService
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly ILogger<UserSettingService> _logger;
    private readonly object _fileLock;
    private readonly string _settingsFilePath;
    private readonly ISecureSettingsStore _secretStore;

    private UserSetting _currentSetting = new();

    /// <summary>
    /// 当前用户设置
    /// </summary>
    public UserSetting CurrentSetting => _currentSetting;

    public UserSettingService(ILogger<UserSettingService>? logger = null)
        : this(
            Path.Combine(FileSystem.AppDataDirectory, AppInfo.UserSettingsFileName),
            new SecureSettingsStore(AppInfo.UserSecretsStoreName, FileSystem.AppDataDirectory),
            logger)
    {
    }

    internal UserSettingService(
        string settingsFilePath,
        ISecureSettingsStore secretStore,
        ILogger<UserSettingService>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(secretStore);

        _settingsFilePath = Path.GetFullPath(settingsFilePath);
        _fileLock = FileLocks.GetOrAdd(_settingsFilePath, static _ => new object());
        _secretStore = secretStore;
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
                string? legacyJson = null;
                if (File.Exists(_settingsFilePath))
                {
                    legacyJson = File.ReadAllText(_settingsFilePath);
                    _currentSetting = JsonSerializer.Deserialize<UserSetting>(legacyJson) ?? new UserSetting();
                }
                else
                {
                    _currentSetting = new UserSetting();
                }

                var migratedLegacySecrets = LoadSecrets(_currentSetting, legacyJson);
                if (migratedLegacySecrets)
                    SaveSettings();

                // 如果日志路径为空，设置为默认日志目录（与启动阶段保持一致）
                if (string.IsNullOrWhiteSpace(_currentSetting.LogPath))
                {
                    _currentSetting.LogPath = Path.Combine(FileSystem.AppDataDirectory, AppInfo.LogsDirectoryName);
                }
            }
            catch (SecureSettingsException ex)
            {
                _logger.LogCritical(ex, "操作系统安全存储不可用，拒绝以空 Secret 继续启动设置服务");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载设置时出错");

                // 普通设置损坏时回退默认值；安全存储失败必须向上传播。
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

                // 先写安全存储，成功后再写不含 Secret 的普通设置，避免界面显示“已保存”但密钥丢失。
                _secretStore.Write(UserSecrets.From(_currentSetting));

                // 先写临时文件，再原子替换，避免进程中断留下半个 JSON。
                var json = JsonSerializer.Serialize(_currentSetting, new JsonSerializerOptions { WriteIndented = true });
                var tempFilePath = _settingsFilePath + ".tmp";
                File.WriteAllText(tempFilePath, json);
                File.Move(tempFilePath, _settingsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存设置时出错");
                throw;
            }
        }
    }

    /// <summary>
    /// 更新设置并保存
    /// </summary>
    /// <param name="setting">新的用户设置</param>
    public void UpdateSettings(UserSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        lock (_fileLock)
        {
            _currentSetting = setting;
            SaveSettings();
        }
    }

    /// <inheritdoc />
    public void UpdateSetting(Action<UserSetting> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        // Monitor 对同一线程可重入：变更与保存共用 _fileLock，
        // 保证跨线程变更不会与序列化/文件替换交错
        lock (_fileLock)
        {
            mutate(_currentSetting);
            SaveSettings();
        }
    }

    private bool LoadSecrets(UserSetting setting, string? legacyJson)
    {
        LegacyUserSecrets? legacySecrets = null;
        if (!string.IsNullOrWhiteSpace(legacyJson))
        {
            // JsonIgnore 会阻止旧 Secret 进入当前设置，因此仅在迁移阶段用独立 DTO 检测一次。
            legacySecrets = JsonSerializer.Deserialize<LegacyUserSecrets>(legacyJson);
        }

        var containsLegacySecrets = legacySecrets?.HasAnyValue() == true;
        if (_secretStore.Read<UserSecrets>() is { } storedSecrets)
        {
            storedSecrets.ApplyTo(setting);
            if (containsLegacySecrets)
                _logger.LogInformation("检测到普通设置文件仍含旧版明文 Secret，正在净化文件");
            return containsLegacySecrets;
        }

        if (!containsLegacySecrets)
            return false;

        legacySecrets!.ApplyTo(setting);
        _logger.LogInformation("检测到旧版明文 Secret，正在迁移到操作系统安全存储");
        return true;
    }

    private sealed record UserSecrets(
        Dictionary<string, string> ProviderApiKeys,
        string EmbeddingApiKey,
        string ZhiTuApiToken,
        string CoinGeckoApiKey,
        string BinanceApiKey,
        string BinanceSecretKey,
        string WebSearchApiKey)
    {
        public static UserSecrets From(UserSetting setting) => new(
            new Dictionary<string, string>(setting.ProviderApiKeys, StringComparer.Ordinal),
            setting.EmbeddingApiKey,
            setting.ZhiTuApiToken,
            setting.CoinGeckoApiKey,
            setting.BinanceApiKey,
            setting.BinanceSecretKey,
            setting.WebSearchApiKey);

        public void ApplyTo(UserSetting setting)
        {
            setting.ProviderApiKeys = new Dictionary<string, string>(ProviderApiKeys, StringComparer.Ordinal);
            setting.EmbeddingApiKey = EmbeddingApiKey;
            setting.ZhiTuApiToken = ZhiTuApiToken;
            setting.CoinGeckoApiKey = CoinGeckoApiKey;
            setting.BinanceApiKey = BinanceApiKey;
            setting.BinanceSecretKey = BinanceSecretKey;
            setting.WebSearchApiKey = WebSearchApiKey;
        }
    }

    private sealed class LegacyUserSecrets
    {
        public Dictionary<string, string>? ProviderApiKeys { get; set; }
        public string? EmbeddingApiKey { get; set; }
        public string? ZhiTuApiToken { get; set; }
        public string? CoinGeckoApiKey { get; set; }
        public string? BinanceApiKey { get; set; }
        public string? BinanceSecretKey { get; set; }
        public string? WebSearchApiKey { get; set; }

        public bool HasAnyValue() =>
            ProviderApiKeys is { Count: > 0 } ||
            !string.IsNullOrWhiteSpace(EmbeddingApiKey) ||
            !string.IsNullOrWhiteSpace(ZhiTuApiToken) ||
            !string.IsNullOrWhiteSpace(CoinGeckoApiKey) ||
            !string.IsNullOrWhiteSpace(BinanceApiKey) ||
            !string.IsNullOrWhiteSpace(BinanceSecretKey) ||
            !string.IsNullOrWhiteSpace(WebSearchApiKey);

        public void ApplyTo(UserSetting setting)
        {
            setting.ProviderApiKeys = ProviderApiKeys is null
                ? []
                : new Dictionary<string, string>(ProviderApiKeys, StringComparer.Ordinal);
            setting.EmbeddingApiKey = EmbeddingApiKey ?? string.Empty;
            setting.ZhiTuApiToken = ZhiTuApiToken ?? string.Empty;
            setting.CoinGeckoApiKey = CoinGeckoApiKey ?? string.Empty;
            setting.BinanceApiKey = BinanceApiKey ?? string.Empty;
            setting.BinanceSecretKey = BinanceSecretKey ?? string.Empty;
            setting.WebSearchApiKey = WebSearchApiKey ?? string.Empty;
        }
    }

    /// <summary>
    /// 重置设置为默认值
    /// </summary>
    public void ResetSettings()
    {
        lock (_fileLock)
        {
            _currentSetting = new UserSetting();
            SaveSettings();
        }
    }
}
