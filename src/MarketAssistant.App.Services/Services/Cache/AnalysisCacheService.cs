using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Cache;

/// <summary>
/// 分析结果缓存服务（彻底重构版）
/// 缓存完整的 MarketAnalysisReport，更符合业务逻辑
/// </summary>
public class AnalysisCacheService : IAnalysisCacheService
{
    private readonly ILogger<AnalysisCacheService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly MarketContext _marketContext;
    private readonly IUserSettingService _userSettingService;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(2);

    public AnalysisCacheService(
        ILogger<AnalysisCacheService> logger,
        IMemoryCache memoryCache,
        MarketContext marketContext,
        IUserSettingService userSettingService)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _marketContext = marketContext;
        _userSettingService = userSettingService;
    }

    /// <summary>
    /// 获取缓存的市场分析报告。
    /// 配置指纹（ModelId + 启用的分析师角色）变更后缓存自动失效。
    /// </summary>
    public Task<MarketAnalysisReport?> GetCachedAnalysisAsync(string assetSymbol)
    {
        if (string.IsNullOrWhiteSpace(assetSymbol))
        {
            throw new ArgumentNullException(nameof(assetSymbol));
        }

        var cacheKey = GenerateCacheKey(assetSymbol);
        var configFingerprint = GetConfigFingerprint();

        if (_memoryCache.TryGetValue(cacheKey, out CachedEntry? entry))
        {
            // 配置指纹不一致 → 视为缓存失效（用户修改了模型或启用的分析师）
            if (entry != null && entry.ConfigFingerprint == configFingerprint)
            {
                _logger.LogInformation("从缓存获取分析报告: {AssetSymbol}, 分析师数量: {Count}",
                    assetSymbol, entry.Report?.AnalystMessages.Count ?? 0);
                return Task.FromResult(entry.Report);
            }

            // 配置已变更，清除过期缓存
            _memoryCache.Remove(cacheKey);
            _logger.LogInformation("配置已变更，清除过期缓存: {AssetSymbol}", assetSymbol);
        }

        _logger.LogInformation("缓存未命中: {AssetSymbol}", assetSymbol);
        return Task.FromResult<MarketAnalysisReport?>(null);
    }

    /// <summary>
    /// 缓存市场分析报告，同时记录当前配置指纹用于后续失效判断
    /// </summary>
    public Task CacheAnalysisAsync(string assetSymbol, MarketAnalysisReport report)
    {
        if (string.IsNullOrWhiteSpace(assetSymbol))
        {
            throw new ArgumentNullException(nameof(assetSymbol));
        }

        ArgumentNullException.ThrowIfNull(report);

        var cacheKey = GenerateCacheKey(assetSymbol);
        var entry = new CachedEntry(report, GetConfigFingerprint());

        _memoryCache.Set(cacheKey, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheExpiration,
            SlidingExpiration = TimeSpan.FromMinutes(30),
            Priority = CacheItemPriority.Normal
        });

        _logger.LogInformation(
            "已缓存分析报告: {AssetSymbol}, 分析师数量: {Count}, 过期时间: {Expiration}",
            assetSymbol,
            report.AnalystMessages.Count,
            _cacheExpiration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 清除指定标的的缓存
    /// </summary>
    public Task ClearCacheAsync(string assetSymbol)
    {
        if (string.IsNullOrWhiteSpace(assetSymbol))
        {
            throw new ArgumentNullException(nameof(assetSymbol));
        }

        var cacheKey = GenerateCacheKey(assetSymbol);
        _memoryCache.Remove(cacheKey);
        _logger.LogInformation("已清除缓存: {AssetSymbol}", assetSymbol);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 生成缓存键（含市场类型，避免跨市场碰撞）。
    /// 键格式由 <see cref="CacheKeys.GetAnalysisReportKey"/> 统一管理。
    /// </summary>
    private string GenerateCacheKey(string assetSymbol)
    {
        return CacheKeys.GetAnalysisReportKey(_marketContext.CurrentMarket, assetSymbol);
    }

    /// <summary>
    /// 生成配置指纹：ModelId + 启用的分析师角色排序后的 JSON。
    /// 任一配置变更后指纹不同，缓存自动失效。
    /// </summary>
    private string GetConfigFingerprint()
    {
        var setting = _userSettingService.CurrentSetting;
        var enabledRoles = setting.EnabledAnalystRoles
            .Where(kv => kv.Value)
            .Select(kv => kv.Key)
            .OrderBy(s => s);
        return $"{setting.ModelId}|{string.Join(",", enabledRoles)}";
    }

    /// <summary>
    /// 缓存条目：报告 + 生成时的配置指纹
    /// </summary>
    private sealed record CachedEntry(MarketAnalysisReport Report, string ConfigFingerprint);

    public void Dispose()
    {
        // IMemoryCache 由 DI 容器管理，无需手动释放
    }
}
