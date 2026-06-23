using MarketAssistant.Infrastructure.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services;

/// <summary>
/// 时序知识图谱服务（SQLite）。
/// 存储实体-关系三元组（subject, predicate, object），每个三元组有有效时间窗口。
/// 参考 MemPalace 的时序知识图谱设计，适配金融投资领域。
/// </summary>
public class UserKnowledgeGraphService : SqliteServiceBase
{
    public UserKnowledgeGraphService(ILogger<UserKnowledgeGraphService> logger)
        : base("knowledge_graph.db", logger)
    {
    }

    /// <summary>
    /// 添加一条三元组（实体-关系-实体），带有效起始时间
    /// </summary>
    public async Task AddTripleAsync(
        string subject, string predicate, string obj,
        string? validFrom = null, string? metadata = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO triples (subject, predicate, object, valid_from, metadata, created_at)
            VALUES (@subject, @predicate, @object, @validFrom, @metadata, @createdAt)
            """;
        cmd.Parameters.AddWithValue("@subject", subject);
        cmd.Parameters.AddWithValue("@predicate", predicate);
        cmd.Parameters.AddWithValue("@object", obj);
        cmd.Parameters.AddWithValue("@validFrom", validFrom ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@metadata", (object?)metadata ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        Logger.LogDebug("知识图谱新增三元组: {S} --[{P}]--> {O}", subject, predicate, obj);
    }

    /// <summary>
    /// 查询实体的所有当前有效关系
    /// </summary>
    public async Task<List<KnowledgeTriple>> QueryEntityAsync(
        string entity, string? asOf = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        var dateFilter = asOf ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, subject, predicate, object, valid_from, valid_to, metadata, created_at
            FROM triples
            WHERE (subject = @entity OR object = @entity)
              AND valid_from <= @date
              AND (valid_to IS NULL OR valid_to >= @date)
            ORDER BY valid_from DESC
            """;
        cmd.Parameters.AddWithValue("@entity", entity);
        cmd.Parameters.AddWithValue("@date", dateFilter);

        return await ReadTriplesAsync(cmd, ct);
    }

    /// <summary>
    /// 使三元组过期（设置 valid_to）
    /// </summary>
    public async Task InvalidateAsync(
        string subject, string predicate, string obj,
        string? ended = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE triples SET valid_to = @ended
            WHERE subject = @subject AND predicate = @predicate AND object = @object
              AND valid_to IS NULL
            """;
        cmd.Parameters.AddWithValue("@subject", subject);
        cmd.Parameters.AddWithValue("@predicate", predicate);
        cmd.Parameters.AddWithValue("@object", obj);
        cmd.Parameters.AddWithValue("@ended", ended ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        await cmd.ExecuteNonQueryAsync(ct);

        Logger.LogDebug("知识图谱三元组过期: {S} --[{P}]--> {O}", subject, predicate, obj);
    }

    /// <summary>
    /// 获取实体的时间线（所有历史关系，含已过期的）
    /// </summary>
    public async Task<List<KnowledgeTriple>> TimelineAsync(
        string entity, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, subject, predicate, object, valid_from, valid_to, metadata, created_at
            FROM triples
            WHERE subject = @entity OR object = @entity
            ORDER BY valid_from ASC
            """;
        cmd.Parameters.AddWithValue("@entity", entity);

        return await ReadTriplesAsync(cmd, ct);
    }

    private static async Task<List<KnowledgeTriple>> ReadTriplesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var result = new List<KnowledgeTriple>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new KnowledgeTriple
            {
                Id = reader.GetInt64(0),
                Subject = reader.GetString(1),
                Predicate = reader.GetString(2),
                Object = reader.GetString(3),
                ValidFrom = reader.GetString(4),
                ValidTo = reader.IsDBNull(5) ? null : reader.GetString(5),
                Metadata = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetString(7)
            });
        }
        return result;
    }

    protected override async Task InitializeDatabaseAsync()
    {
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS triples (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    subject TEXT NOT NULL,
                    predicate TEXT NOT NULL,
                    object TEXT NOT NULL,
                    valid_from TEXT NOT NULL,
                    valid_to TEXT,
                    metadata TEXT,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_triples_subject ON triples(subject);
                CREATE INDEX IF NOT EXISTS idx_triples_object ON triples(object);
                CREATE INDEX IF NOT EXISTS idx_triples_predicate ON triples(predicate);
                CREATE INDEX IF NOT EXISTS idx_triples_validity ON triples(valid_from, valid_to);
                """;
            await cmd.ExecuteNonQueryAsync();
            Logger.LogInformation("知识图谱数据库初始化完成");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "初始化知识图谱数据库失败");
            throw;
        }
    }
}

/// <summary>
/// 知识图谱三元组
/// </summary>
public record KnowledgeTriple
{
    public long Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Predicate { get; init; } = string.Empty;
    public string Object { get; init; } = string.Empty;
    public string ValidFrom { get; init; } = string.Empty;
    public string? ValidTo { get; init; }
    public string? Metadata { get; init; }
    public string CreatedAt { get; init; } = string.Empty;

    public bool IsActive => ValidTo is null;
}

