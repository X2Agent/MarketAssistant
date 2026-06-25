using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.History;

/// <summary>
/// 资产历史记录服务：封装 SQLite 持久化与容量控制逻辑。
/// 通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型。
/// </summary>
public sealed class AssetHistoryService : SqliteServiceBase, IAssetHistoryService
{
    private const int MaxHistoryCount = 10;
    private readonly ILogger<AssetHistoryService> _logger;
    private readonly MarketType _marketType;
    private readonly string _marketLabel;

    public AssetHistoryService([ServiceKey] MarketType marketType, ILogger<AssetHistoryService> logger)
        : base(logger)
    {
        _marketType = marketType;
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
            CREATE TABLE IF NOT EXISTS recent_assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                market_type INTEGER NOT NULL,
                viewed_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_recent_mt_time ON recent_assets(market_type, viewed_at DESC);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddHistoryAsync(AssetItem asset, CancellationToken cancellationToken = default)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Code))
            return;

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);

            // 移除同 code 的旧记录，再插入新记录
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM recent_assets WHERE code = @code AND market_type = @marketType";
                delCmd.Parameters.AddWithValue("@code", asset.Code);
                delCmd.Parameters.AddWithValue("@marketType", (int)_marketType);
                await delCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insCmd = conn.CreateCommand())
            {
                insCmd.CommandText = """
                    INSERT INTO recent_assets (code, name, market_type, viewed_at)
                    VALUES (@code, @name, @marketType, @viewedAt)
                    """;
                insCmd.Parameters.AddWithValue("@code", asset.Code);
                insCmd.Parameters.AddWithValue("@name", asset.Name);
                insCmd.Parameters.AddWithValue("@marketType", (int)_marketType);
                insCmd.Parameters.AddWithValue("@viewedAt", DateTime.UtcNow.ToString("O"));
                await insCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 截断：只保留最新的 MaxHistoryCount 条
            await using (var trimCmd = conn.CreateCommand())
            {
                trimCmd.CommandText = """
                    DELETE FROM recent_assets
                    WHERE market_type = @marketType
                      AND id NOT IN (
                          SELECT id FROM recent_assets
                          WHERE market_type = @marketType
                          ORDER BY viewed_at DESC
                          LIMIT @maxCount
                      )
                    """;
                trimCmd.Parameters.AddWithValue("@marketType", (int)_marketType);
                trimCmd.Parameters.AddWithValue("@maxCount", MaxHistoryCount);
                await trimCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("已添加{Market}到历史记录: {Code}", _marketLabel, asset.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存历史记录时出错: {Message}", ex.Message);
        }
    }

    public async Task<List<AssetItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT code, name FROM recent_assets
                WHERE market_type = @marketType
                ORDER BY viewed_at DESC
                LIMIT @maxCount
                """;
            cmd.Parameters.AddWithValue("@marketType", (int)_marketType);
            cmd.Parameters.AddWithValue("@maxCount", MaxHistoryCount);

            var list = new List<AssetItem>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new AssetItem
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1)
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取历史记录时出错: {Message}", ex.Message);
            return [];
        }
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM recent_assets WHERE market_type = @marketType";
            cmd.Parameters.AddWithValue("@marketType", (int)_marketType);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("已清空{Market}历史记录", _marketLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清空历史记录时出错: {Message}", ex.Message);
        }
    }
}
