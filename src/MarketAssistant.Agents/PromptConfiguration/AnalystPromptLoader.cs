using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MarketAssistant.Agents.PromptConfiguration;

/// <summary>
/// 分析师提示词加载器，从独立 YAML 配置文件加载每个分析师的提示词和参数配置。
/// 每个分析师对应一个文件：config/prompts/{AgentName}.yaml（如 FinancialAnalyst.yaml）。
/// 支持热加载：每次访问时检查文件修改时间，文件变更后自动重新加载。
/// </summary>
public class AnalystPromptLoader
{
    private static readonly string PromptsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config", "prompts");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .WithCaseInsensitivePropertyMatching()
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ILogger<AnalystPromptLoader> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public AnalystPromptLoader(ILogger<AnalystPromptLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取指定分析师的提示词配置。
    /// 自动从 config/prompts/{analystName}.yaml 加载，支持热重载。
    /// 文件缺失或关键字段（name、instructions）为空时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public AnalystPromptConfig GetConfig(string analystName)
    {
        var filePath = Path.Combine(PromptsDir, $"{analystName}.yaml");

        if (!File.Exists(filePath))
            throw new InvalidOperationException($"分析师配置文件不存在: {filePath}");

        var lastWrite = File.GetLastWriteTimeUtc(filePath);

        if (_cache.TryGetValue(analystName, out var cached) && lastWrite <= cached.LoadTime)
            return cached.Config;

        AnalystPromptConfig? config;
        try
        {
            var yaml = File.ReadAllText(filePath);
            config = Deserializer.Deserialize<AnalystPromptConfig>(yaml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载分析师配置失败: {Path}", filePath);
            if (cached is not null)
            {
                _logger.LogWarning("将继续使用上一次成功加载的配置: {Name}", analystName);
                return cached.Config;
            }
            throw new InvalidOperationException($"分析师 {analystName} 配置加载失败: {filePath}", ex);
        }

        if (string.IsNullOrWhiteSpace(config.Name))
            throw new InvalidOperationException($"分析师 {analystName} 配置缺少关键信息: name 为空");
        if (string.IsNullOrWhiteSpace(config.Instructions))
            throw new InvalidOperationException($"分析师 {analystName} 配置缺少关键信息: instructions 为空");

        _cache[analystName] = new CacheEntry(config, lastWrite);
        _logger.LogInformation("已加载分析师配置: {Name} ← {Path}", analystName, filePath);
        return config;
    }

    private sealed record CacheEntry(AnalystPromptConfig Config, DateTime LoadTime);
}
