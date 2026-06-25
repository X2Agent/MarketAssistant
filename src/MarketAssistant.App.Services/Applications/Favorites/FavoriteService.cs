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
public sealed class FavoriteService : SqliteServiceBase, IFavoriteService
{
    private readonly IAssetInfoService _assetInfoService;
    private readonly ILogger<FavoriteService> _logger;
    private readonly MarketType _marketType;
    private readonly string _marketLabel;

    public FavoriteService(
        [ServiceKey] MarketType marketType,
        IServiceProvider serviceProvider,
        ILogger<FavoriteService> logger)
        : base(logger)
    {
        _marketType = marketType;
        _assetInfoService = serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketType);
        _logger = logger;
        _marketLabel = marketType switch
        {
            MarketType.AShare => "A股",
            MarketType.Crypto => "虚拟币",
            _ => marketType.ToString()
        };
    }

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
    }

    public async Task AddFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        code = code.Trim();
        market = market.Trim();

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
        code = code.Trim();
        market = market.Trim();

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM favorite_assets WHERE code = @code AND market_type = @marketType
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
        code = code.Trim();
        market = market.Trim();

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(1) FROM favorite_assets WHERE code = @code AND market_type = @marketType
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

        var tasks = favoritesCodes.Select(async favorite =>
        {
            try
            {
                return await _assetInfoService.GetAssetInfoAsync(favorite.Code, favorite.Market, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取{Market} {Code} 最新数据时出错: {Message}", _marketLabel, favorite.Code, ex.Message);
                return CreateFallbackAssetInfo(favorite);
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(result => result != null).ToList();
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
