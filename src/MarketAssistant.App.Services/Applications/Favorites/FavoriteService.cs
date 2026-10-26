using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Applications.Favorites;

/// <summary>
/// 收藏服务：封装 SQLite 持久化与最新行情查询的共同行为。
/// 通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型。
/// </summary>
public sealed class FavoriteService(
    [ServiceKey] MarketType marketType,
    IServiceProvider serviceProvider,
    ILogger<FavoriteService> logger) : SqliteServiceBase(logger), IFavoriteService
{
    private readonly IAssetInfoService _assetInfoService = serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketType);
    private readonly ILogger<FavoriteService> _logger = logger;
    private readonly MarketType _marketType = marketType;
    private readonly string _marketLabel = marketType switch
    {
        MarketType.AShare => "A股",
        MarketType.Crypto => "虚拟币",
        _ => marketType.ToString()
    };

    protected override async Task InitializeDatabaseAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS favorite_assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL,
                market TEXT NOT NULL,
                market_type INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_fav_code_mt ON favorite_assets(code, market_type);
            CREATE INDEX IF NOT EXISTS idx_fav_mt ON favorite_assets(market_type);
            """;
        await cmd.ExecuteNonQueryAsync();

        // 存量数据归一化迁移（幂等）：历史数据存在小写/带 SH/SZ/BJ 前缀的代码，
        // 与归一化后的查询（大写、无前缀）无法匹配，导致详情页收藏状态判定失败。
        // 顺序：先按归一化后的键去重（保留最早一条），再统一大写，最后剥离 A 股交易所前缀。
        // BJ 与 SH/SZ 同规则：前缀剥离逻辑统一在 StockSymbolConverter，此处保持同步。
        await using var migrateCmd = conn.CreateCommand();
        migrateCmd.CommandText = """
            DELETE FROM favorite_assets
            WHERE id NOT IN (SELECT MIN(id) FROM favorite_assets GROUP BY UPPER(code), market_type);
            UPDATE favorite_assets SET code = UPPER(code);
            UPDATE favorite_assets SET code = SUBSTR(code, 3)
            WHERE market_type = 0 AND SUBSTR(code, 1, 2) IN ('SH', 'SZ', 'BJ');
            """;
        await migrateCmd.ExecuteNonQueryAsync();

        // 虚拟币存量数据归一化迁移（幂等）：将历史遗留的基础币种（如 BTC）统一转为币安交易对格式（如 BTCUSDT）
        var cryptoRows = new List<(long Id, string Code)>();
        await using (var selectCryptoCmd = conn.CreateCommand())
        {
            selectCryptoCmd.CommandText = "SELECT id, code FROM favorite_assets WHERE market_type = 1 ORDER BY id";
            await using var reader = await selectCryptoCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                cryptoRows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        var seenCryptoCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, rawCode) in cryptoRows)
        {
            var normalizedCode = FavoriteCodeNormalizer.Normalize(rawCode, MarketType.Crypto);
            if (!seenCryptoCodes.Add(normalizedCode))
            {
                await using var delCmd = conn.CreateCommand();
                delCmd.CommandText = "DELETE FROM favorite_assets WHERE id = @id";
                delCmd.Parameters.AddWithValue("@id", id);
                await delCmd.ExecuteNonQueryAsync();
            }
            else if (!string.Equals(rawCode, normalizedCode, StringComparison.Ordinal))
            {
                await using var updCmd = conn.CreateCommand();
                updCmd.CommandText = "UPDATE favorite_assets SET code = @code WHERE id = @id";
                updCmd.Parameters.AddWithValue("@code", normalizedCode);
                updCmd.Parameters.AddWithValue("@id", id);
                await updCmd.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task AddFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        code = code.Trim();
        market = market.Trim();
        // 写入前统一归一化，保证与查询端（详情页/首页）的代码格式一致
        code = FavoriteCodeNormalizer.Normalize(code, _marketType);

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO favorite_assets (code, market, market_type, created_at)
                VALUES (@code, @market, @marketType, @createdAt)
                """;
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@market", market);
            cmd.Parameters.AddWithValue("@marketType", (int)_marketType);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
            _logger.LogInformation("已添加{Market}到收藏: {Code}", _marketLabel, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存收藏{Market}时出错: {Message}", _marketLabel, ex.Message);
        }
    }

    public async Task RemoveFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
    {
        // 与写入端共用归一化入口：调用方传入 sh600519 等未归一化代码时也能命中
        code = FavoriteCodeNormalizer.Normalize(code, _marketType);
        market = market.Trim();

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM favorite_assets WHERE UPPER(code) = @code AND market_type = @marketType
                """;
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@marketType", (int)_marketType);
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);

            if (affected > 0)
            {
                WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
                _logger.LogInformation("已从收藏中移除{Market}: {Code}", _marketLabel, code);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除收藏{Market}时出错: {Message}", _marketLabel, ex.Message);
        }
    }

    public async Task<bool> IsFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
    {
        // 与写入端共用归一化入口：调用方传入 sh600519 等未归一化代码时也能正确判定
        code = FavoriteCodeNormalizer.Normalize(code, _marketType);
        market = market.Trim();

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(1) FROM favorite_assets WHERE UPPER(code) = @code AND market_type = @marketType
                """;
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@marketType", (int)_marketType);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result is long count && count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查收藏{Market}时出错: {Message}", _marketLabel, ex.Message);
            return false;
        }
    }

    public async Task<List<FavoriteAsset>> GetFavoritesCodesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT code, market FROM favorite_assets WHERE market_type = @marketType ORDER BY created_at
                """;
            cmd.Parameters.AddWithValue("@marketType", (int)_marketType);

            var list = new List<FavoriteAsset>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new FavoriteAsset
                {
                    Code = reader.GetString(0).Trim(),
                    Market = reader.GetString(1).Trim()
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取收藏{Market}时出错: {Message}", _marketLabel, ex.Message);
            return [];
        }
    }

    public async Task<List<AssetInfo>> GetFavoritesWithLatestDataAsync(CancellationToken cancellationToken = default)
    {
        var favoritesCodes = await GetFavoritesCodesAsync(cancellationToken);
        if (favoritesCodes.Count == 0)
            return [];

        try
        {
            var fetched = await _assetInfoService.GetAssetInfosAsync(favoritesCodes, cancellationToken);
            var results = new List<AssetInfo>(favoritesCodes.Count);
            for (var i = 0; i < favoritesCodes.Count; i++)
            {
                var info = i < fetched.Count ? fetched[i] : null;
                results.Add(info ?? CreateFallbackAssetInfo(favoritesCodes[i]));
            }
            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量获取{Market}收藏最新数据时出错: {Message}", _marketLabel, ex.Message);
            return [.. favoritesCodes.Select(CreateFallbackAssetInfo)];
        }
    }

    public async Task ClearFavoritesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM favorite_assets WHERE market_type = @marketType";
            cmd.Parameters.AddWithValue("@marketType", (int)_marketType);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
            _logger.LogInformation("已清空所有收藏{Market}", _marketLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清空收藏{Market}时出错: {Message}", _marketLabel, ex.Message);
        }
    }

    /// <summary>
    /// 行情获取失败时的兜底 AssetInfo：A股使用 Market.Code 形式，虚拟币使用基础币种名称。
    /// </summary>
    private AssetInfo CreateFallbackAssetInfo(FavoriteAsset favorite)
    {
        var displayName = _marketType switch
        {
            MarketType.Crypto => ExtractBaseCurrency(favorite.Code),
            _ => string.IsNullOrWhiteSpace(favorite.Market)
                ? favorite.Code
                : $"{favorite.Market}.{favorite.Code}"
        };

        return new AssetInfo
        {
            Code = favorite.Code,
            Market = favorite.Market,
            Name = displayName,
            MarketType = _marketType
        };
    }
}
