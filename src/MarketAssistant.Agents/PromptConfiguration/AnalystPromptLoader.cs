using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MarketAssistant.Agents.PromptConfiguration;

/// <summary>
/// 分析师提示词加载器，从 YAML 配置文件加载分析师的提示词和参数配置。
/// 支持热加载：每次访问时检查文件修改时间，文件变更后自动重新加载。
/// </summary>
public class AnalystPromptLoader
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config", "prompts", "analysts.yaml");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ILogger<AnalystPromptLoader> _logger;
    private Dictionary<string, AnalystPromptConfig>? _cache;
    private DateTime _lastLoadTime;

    public AnalystPromptLoader(ILogger<AnalystPromptLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取指定分析师的提示词配置
    /// </summary>
    public AnalystPromptConfig? GetConfig(string analystName)
    {
        EnsureLoaded();
        if (_cache != null && _cache.TryGetValue(analystName, out var config))
            return config;
        return null;
    }

    /// <summary>
    /// 获取所有分析师的提示词配置
    /// </summary>
    public IReadOnlyDictionary<string, AnalystPromptConfig> GetAllConfigs()
    {
        EnsureLoaded();
        return _cache ?? new Dictionary<string, AnalystPromptConfig>();
    }

    private void EnsureLoaded()
    {
        if (!File.Exists(ConfigPath))
        {
            _logger.LogWarning("分析师提示词配置文件不存在: {Path}，使用内置默认值", ConfigPath);
            return;
        }

        var lastWrite = File.GetLastWriteTimeUtc(ConfigPath);
        if (_cache != null && lastWrite <= _lastLoadTime)
            return;

        try
        {
            var yaml = File.ReadAllText(ConfigPath);
            _cache = Deserializer.Deserialize<Dictionary<string, AnalystPromptConfig>>(yaml);
            _lastLoadTime = lastWrite;
            _logger.LogInformation("加载分析师提示词配置: {Count} 条", _cache?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载分析师提示词配置失败: {Path}", ConfigPath);
            if (_cache == null)
                throw;
            _logger.LogWarning("将继续使用上一次成功加载的配置（{Count} 条）", _cache.Count);
        }
    }
}
