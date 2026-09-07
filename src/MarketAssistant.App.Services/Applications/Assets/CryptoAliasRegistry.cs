using System.Text.RegularExpressions;
using MarketAssistant.Applications.Cache;
using MarketAssistant.DataProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 加密货币别名注册表：动态维护 symbol → (name, coinGeckoId, matchRegex) 映射。
/// 数据源为 CoinGecko top-N 市值币种，缓存后供 Telegram 符号提取、RSS 过滤、CoinGecko ID 解析等场景复用。
/// </summary>
public interface ICryptoAliasRegistry
{
    /// <summary>
    /// 获取 symbol（大写）→ 编译后正则的映射，用于从文本中提取关联币种。
    /// </summary>
    Task<IReadOnlyDictionary<string, Regex>> GetMatchPatternsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 symbol（大写）→ CoinGecko ID 的映射。
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetCoinGeckoIdMapAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 symbol（大写）→ 英文全称的映射。
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetNameMapAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定 symbol 的英文别名列表（含 symbol 自身 + 英文全称），用于 RSS 文本匹配。
    /// </summary>
    Task<List<string>> GetAliasesAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class CryptoAliasRegistry : ICryptoAliasRegistry
{
    private const string CacheKey = "CryptoAliasRegistry_Data";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly CoinGeckoApiService _coinGeckoService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CryptoAliasRegistry> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public CryptoAliasRegistry(
        CoinGeckoApiService coinGeckoService,
        IMemoryCache memoryCache,
        ILogger<CryptoAliasRegistry> logger)
    {
        _coinGeckoService = coinGeckoService;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, Regex>> GetMatchPatternsAsync(CancellationToken cancellationToken = default)
        => (await GetDataAsync(cancellationToken)).Patterns;

    public async Task<IReadOnlyDictionary<string, string>> GetCoinGeckoIdMapAsync(CancellationToken cancellationToken = default)
        => (await GetDataAsync(cancellationToken)).CoinGeckoIds;

    public async Task<IReadOnlyDictionary<string, string>> GetNameMapAsync(CancellationToken cancellationToken = default)
        => (await GetDataAsync(cancellationToken)).Names;

    public async Task<List<string>> GetAliasesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var names = await GetNameMapAsync(cancellationToken);
        var upperSymbol = symbol.ToUpperInvariant();
        var aliases = new List<string> { upperSymbol };
        if (names.TryGetValue(upperSymbol, out var name))
            aliases.Add(name.ToUpperInvariant());
        return aliases;
    }

    private async Task<RegistryData> GetDataAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(CacheKey, out RegistryData? cached) && cached is not null)
            return cached;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_memoryCache.TryGetValue(CacheKey, out cached) && cached is not null)
                return cached;

            var data = await BuildRegistryDataAsync(cancellationToken);
            _memoryCache.Set(CacheKey, data, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            });
            return data;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<RegistryData> BuildRegistryDataAsync(CancellationToken cancellationToken)
    {
        var patterns = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);
        var coinGeckoIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var markets = await _coinGeckoService.GetCoinsMarketsAsync(
                perPage: 100, cancellationToken: cancellationToken);

            foreach (var coin in markets)
            {
                var symbol = coin.Symbol.ToUpperInvariant();
                if (string.IsNullOrEmpty(symbol) || patterns.ContainsKey(symbol))
                    continue;

                coinGeckoIds[symbol] = coin.Id;
                names[symbol] = coin.Name;

                var parts = new List<string> { $@"\b{Regex.Escape(symbol)}\b" };
                if (!string.IsNullOrWhiteSpace(coin.Name)
                    && !string.Equals(coin.Name, symbol, StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add($@"\b{Regex.Escape(coin.Name)}\b");
                }

                var pattern = string.Join("|", parts);
                patterns[symbol] = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            _logger.LogInformation("CryptoAliasRegistry 已加载 {Count} 个币种别名", patterns.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CryptoAliasRegistry 从 CoinGecko 加载失败，回退到静态列表");
            BuildFallbackData(patterns, coinGeckoIds, names);
        }

        return new RegistryData(patterns, coinGeckoIds, names);
    }

    /// <summary>
    /// CoinGecko 不可用时的兜底数据，覆盖最常见的主流币种。
    /// </summary>
    private static void BuildFallbackData(
        Dictionary<string, Regex> patterns,
        Dictionary<string, string> coinGeckoIds,
        Dictionary<string, string> names)
    {
        var fallback = new (string Symbol, string Name, string CoinGeckoId)[]
        {
            ("BTC", "Bitcoin", "bitcoin"),
            ("ETH", "Ethereum", "ethereum"),
            ("USDT", "Tether", "tether"),
            ("BNB", "Binance Coin", "binancecoin"),
            ("SOL", "Solana", "solana"),
            ("XRP", "Ripple", "ripple"),
            ("ADA", "Cardano", "cardano"),
            ("DOGE", "Dogecoin", "dogecoin"),
            ("DOT", "Polkadot", "polkadot"),
            ("AVAX", "Avalanche", "avalanche-2"),
            ("LINK", "Chainlink", "chainlink"),
            ("UNI", "Uniswap", "uniswap"),
            ("SHIB", "Shiba Inu", "shiba-inu"),
            ("LTC", "Litecoin", "litecoin"),
            ("MATIC", "Polygon", "matic-network"),
            ("ATOM", "Cosmos", "cosmos"),
            ("ARB", "Arbitrum", "arbitrum"),
            ("OP", "Optimism", "optimism"),
            ("APT", "Aptos", "aptos"),
            ("SUI", "Sui", "sui"),
            ("TON", "Toncoin", "toncoin"),
            ("TRX", "TRON", "tron"),
            ("NEAR", "NEAR Protocol", "near"),
            ("PEPE", "Pepe", "pepe"),
        };

        foreach (var (symbol, name, id) in fallback)
        {
            coinGeckoIds[symbol] = id;
            names[symbol] = name;

            var parts = new List<string> { $@"\b{Regex.Escape(symbol)}\b" };
            if (!string.Equals(name, symbol, StringComparison.OrdinalIgnoreCase))
                parts.Add($@"\b{Regex.Escape(name)}\b");

            patterns[symbol] = new Regex(
                string.Join("|", parts),
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }

    private sealed record RegistryData(
        IReadOnlyDictionary<string, Regex> Patterns,
        IReadOnlyDictionary<string, string> CoinGeckoIds,
        IReadOnlyDictionary<string, string> Names);
}
