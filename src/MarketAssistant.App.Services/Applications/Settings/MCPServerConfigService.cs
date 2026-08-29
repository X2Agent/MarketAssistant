using System.Collections.Concurrent;
using MarketAssistant.Services.Settings;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// MCP服务器配置服务，提供对MCPServerConfig的统一访问和管理。
/// 所有读写操作通过锁保护，确保 UI 线程与后台线程并发访问时的线程安全。
/// </summary>
public class MCPServerConfigService
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private List<MCPServerConfig> _serverConfigs = new();
    private readonly object _lock;
    private readonly ISecureSettingsStore _secretStore;
    private readonly string _configFilePath;

    /// <summary>
    /// 当前所有MCP服务器配置（返回副本，避免外部修改影响内部状态）
    /// </summary>
    public List<MCPServerConfig> ServerConfigs
    {
        get
        {
            lock (_lock)
            {
                return _serverConfigs.ToList();
            }
        }
    }

    public MCPServerConfigService()
        : this(
            Path.Combine(FileSystem.AppDataDirectory, AppInfo.MCPServerConfigFileName),
            new SecureSettingsStore(AppInfo.McpSecretsStoreName, FileSystem.AppDataDirectory))
    {
    }

    internal MCPServerConfigService(string configFilePath, ISecureSettingsStore secretStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ArgumentNullException.ThrowIfNull(secretStore);

        _configFilePath = Path.GetFullPath(configFilePath);
        _lock = FileLocks.GetOrAdd(_configFilePath, static _ => new object());
        _secretStore = secretStore;
        LoadConfigs();
    }

    /// <summary>
    /// 从存储中加载配置
    /// </summary>
    public void LoadConfigs()
    {
        lock (_lock)
        {
            string? legacyJson = null;
            if (File.Exists(_configFilePath))
            {
                legacyJson = File.ReadAllText(_configFilePath);
                _serverConfigs = JsonSerializer.Deserialize<List<MCPServerConfig>>(legacyJson) ?? [];
            }

            var legacyConfigs = string.IsNullOrWhiteSpace(legacyJson)
                ? []
                : JsonSerializer.Deserialize<List<LegacyMcpServerSecrets>>(legacyJson) ?? [];
            var migratedSecrets = legacyConfigs
                .Where(config => config.EnvironmentVariables is { Count: > 0 })
                .ToDictionary(
                    config => config.Id,
                    config => new Dictionary<string, string?>(config.EnvironmentVariables!),
                    StringComparer.Ordinal);

            if (_secretStore.Read<Dictionary<string, Dictionary<string, string?>>>() is { } storedEnvironmentVariables)
            {
                ApplyEnvironmentVariables(storedEnvironmentVariables);
                if (migratedSecrets.Count > 0)
                    SaveConfigs();
            }
            else if (migratedSecrets.Count > 0)
            {
                ApplyEnvironmentVariables(migratedSecrets);
                SaveConfigs();
            }
        }
    }

    /// <summary>
    /// 保存配置到存储
    /// </summary>
    public void SaveConfigs()
    {
        lock (_lock)
        {
            var snapshot = _serverConfigs.ToList();

            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var environmentVariables = snapshot.ToDictionary(
                config => config.Id,
                config => new Dictionary<string, string?>(config.EnvironmentVariables),
                StringComparer.Ordinal);
            _secretStore.Write(environmentVariables);

            // 序列化不含环境变量 Secret 的配置对象并原子替换目标文件。
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var tempFilePath = _configFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _configFilePath, overwrite: true);
        }
    }

    /// <summary>
    /// 添加或更新MCP服务器配置
    /// </summary>
    /// <param name="config">MCP服务器配置</param>
    public void AddOrUpdateConfig(MCPServerConfig config)
    {
        lock (_lock)
        {
            // 查找是否已存在相同ID的配置
            int index = _serverConfigs.FindIndex(c => c.Id == config.Id);
            if (index >= 0)
            {
                // 更新现有配置
                _serverConfigs[index] = config;
            }
            else
            {
                // 添加新配置
                _serverConfigs.Add(config);
            }
        }

        // 保存更改
        SaveConfigs();
    }

    /// <summary>
    /// 删除MCP服务器配置
    /// </summary>
    /// <param name="id">配置ID</param>
    public void DeleteConfig(string id)
    {
        lock (_lock)
        {
            // 查找并删除配置
            _serverConfigs.RemoveAll(c => c.Id == id);
        }

        // 保存更改
        SaveConfigs();
    }

    private void ApplyEnvironmentVariables(
        IReadOnlyDictionary<string, Dictionary<string, string?>> environmentVariables)
    {
        foreach (var config in _serverConfigs)
        {
            if (environmentVariables.TryGetValue(config.Id, out var values))
                config.EnvironmentVariables = new Dictionary<string, string?>(values);
        }
    }

    private sealed class LegacyMcpServerSecrets
    {
        public string Id { get; set; } = string.Empty;
        public Dictionary<string, string?>? EnvironmentVariables { get; set; }
    }

    /// <summary>
    /// 获取指定ID的MCP服务器配置
    /// </summary>
    /// <param name="id">配置ID</param>
    /// <returns>MCP服务器配置，如果不存在则返回null</returns>
    public MCPServerConfig? GetConfig(string id)
    {
        lock (_lock)
        {
            return _serverConfigs.FirstOrDefault(c => c.Id == id);
        }
    }
}
