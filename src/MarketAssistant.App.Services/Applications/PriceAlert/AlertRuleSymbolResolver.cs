using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.PriceAlert;

/// <summary>
/// 告警规则符号解析：单标的规则取其 <see cref="PriceAlertRule.AssetCode"/>；
/// 自选股批量规则（<see cref="AlertScope.Watchlist"/>）按市场解析自选股列表。
/// </summary>
/// <remarks>
/// <para>自选股查询失败时向外抛出，由调用方按规则隔离处理——
/// 不得把"数据源故障"降级为空列表，那会让批量规则静默失效而用户毫无感知。</para>
/// <para>自选股集合带 TTL 缓存：价格 tick 每秒到达，逐 tick 查库会把批量规则的评估
/// 变成数据库压力；行情事件处理器是同步回调，冷缓存时只预热并跳过本次 tick。</para>
/// </remarks>
public sealed class AlertRuleSymbolResolver(
    IServiceProvider serviceProvider,
    ILogger<AlertRuleSymbolResolver> logger)
{
    /// <summary>自选股集合缓存时长。自选股变更有独立路径，TTL 兜底新鲜度。</summary>
    private static readonly TimeSpan WatchlistCacheTtl = TimeSpan.FromSeconds(30);

    private readonly object _cacheSync = new();
    private readonly Dictionary<MarketType, (DateTime CachedAtUtc, IReadOnlyList<string> Symbols)> _watchlistCache = [];

    /// <summary>解析单条规则实际评估的行情符号（符号形式与规则的 AssetCode 约定一致，由调用方按市场转换）。</summary>
    public async Task<IReadOnlyList<string>> ResolveSymbolsAsync(
        PriceAlertRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Scope != AlertScope.Watchlist)
        {
            return string.IsNullOrWhiteSpace(rule.AssetCode)
                ? []
                : [rule.AssetCode];
        }

        return await ResolveWatchlistSymbolsAsync(rule.MarketType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析某市场下全部启用规则需要订阅/轮询的符号集合
    /// （单标的规则符号 + 自选股规则展开后的自选股）。
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveAllSymbolsAsync(
        MarketType marketType, IEnumerable<PriceAlertRule> rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var symbols = new List<string>();
        foreach (var rule in rules.Where(r => r.MarketType == marketType && r.Enabled))
        {
            symbols.AddRange(await ResolveSymbolsAsync(rule, cancellationToken).ConfigureAwait(false));
        }

        return [.. symbols.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>带 TTL 缓存的自选股符号解析（异步路径使用，如轮询与指标评估）。</summary>
    public async Task<IReadOnlyList<string>> ResolveWatchlistSymbolsAsync(
        MarketType marketType, CancellationToken cancellationToken = default)
    {
        if (TryGetCached(marketType, out var cached))
            return cached;

        var symbols = await LoadWatchlistSymbolsAsync(marketType, cancellationToken).ConfigureAwait(false);

        lock (_cacheSync)
        {
            _watchlistCache[marketType] = (DateTime.UtcNow, symbols);
        }

        return symbols;
    }

    /// <summary>读取自选股缓存（同步路径使用）。冷缓存或已过期返回 null，调用方应预热并跳过本次评估。</summary>
    public IReadOnlyList<string>? GetCachedWatchlistSymbols(MarketType marketType)
        => TryGetCached(marketType, out var cached) ? cached : null;

    /// <summary>后台预热自选股缓存（冷缓存时由行情事件处理器触发）。</summary>
    public async Task WarmWatchlistCacheAsync(MarketType marketType, CancellationToken cancellationToken = default)
    {
        try
        {
            await ResolveWatchlistSymbolsAsync(marketType, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "预热自选股缓存失败: {MarketType}", marketType);
        }
    }

    /// <summary>自选股变更后调用，立即使缓存失效。</summary>
    public void InvalidateCache()
    {
        lock (_cacheSync)
        {
            _watchlistCache.Clear();
        }
    }

    private bool TryGetCached(MarketType marketType, out IReadOnlyList<string> symbols)
    {
        lock (_cacheSync)
        {
            if (_watchlistCache.TryGetValue(marketType, out var entry)
                && DateTime.UtcNow - entry.CachedAtUtc < WatchlistCacheTtl)
            {
                symbols = entry.Symbols;
                return true;
            }

            symbols = [];
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> LoadWatchlistSymbolsAsync(
        MarketType marketType, CancellationToken cancellationToken)
    {
        var favoriteService = serviceProvider.GetRequiredKeyedService<IFavoriteService>(marketType);
        var favorites = await favoriteService.GetFavoritesCodesAsync(cancellationToken).ConfigureAwait(false);
        return [.. favorites
            .Select(favorite => favorite.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
