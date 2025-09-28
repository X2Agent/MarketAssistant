using MarketAssistant.Applications.Settings;
using System.Text.Json;

namespace MarketAssistant.Infrastructure;

/// <summary>
/// MCP服务器配置服务，提供对MCPServerConfig的统一访问和管理
/// </summary>
public class MCPServerConfigService
{
    private static readonly Lazy<MCPServerConfigService> _instance = new(() => new MCPServerConfigService());

    /// <summary>
    /// 获取MCPServerConfigService的单例实例
    /// </summary>
    public static MCPServerConfigService Instance => _instance.Value;

    private List<MCPServerConfig> _serverConfigs = new();

    /// <summary>
    /// 当前所有MCP服务器配置
    /// </summary>
    public List<MCPServerConfig> ServerConfigs => _serverConfigs;

    // 配置文件路径
    private readonly string _configFilePath = Path.Combine(FileSystem.AppDataDirectory, "mcpservers.json");

    private MCPServerConfigService()
    {
        // 从存储中加载设置
        LoadConfigs();
    }

    /// <summary>
    /// 从存储中加载配置
    /// </summary>
    public void LoadConfigs()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                _serverConfigs = JsonSerializer.Deserialize<List<MCPServerConfig>>(json) ?? new List<MCPServerConfig>();
            }
        }
        catch (Exception ex)
        {
            // 处理异常
            System.Diagnostics.Debug.WriteLine($"加载MCP服务器配置时出错: {ex.Message}");

            // 如果加载失败，使用空列表
            _serverConfigs = new List<MCPServerConfig>();
        }
    }

    /// <summary>
    /// 保存配置到存储
    /// </summary>
    public void SaveConfigs()
    {
        try
        {
            // 确保目录存在
            string directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 序列化配置对象
            string json = JsonSerializer.Serialize(_serverConfigs, new JsonSerializerOptions { WriteIndented = true });

            // 保存到文件
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            // 处理异常
            System.Diagnostics.Debug.WriteLine($"保存MCP服务器配置时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 添加或更新MCP服务器配置
    /// </summary>
    /// <param name="config">MCP服务器配置</param>
    public void AddOrUpdateConfig(MCPServerConfig config)
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

        // 保存更改
        SaveConfigs();
    }

    /// <summary>
    /// 删除MCP服务器配置
    /// </summary>
    /// <param name="id">配置ID</param>
    public void DeleteConfig(string id)
    {
        // 查找并删除配置
        _serverConfigs.RemoveAll(c => c.Id == id);

        // 保存更改
        SaveConfigs();
    }

    /// <summary>
    /// 获取指定ID的MCP服务器配置
    /// </summary>
    /// <param name="id">配置ID</param>
    /// <returns>MCP服务器配置，如果不存在则返回null</returns>
    public MCPServerConfig GetConfig(string id)
    {
        return _serverConfigs.FirstOrDefault(c => c.Id == id);
    }
}