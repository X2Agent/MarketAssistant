using MarketAssistant.Infrastructure.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services;

/// <summary>
/// 用户长期记忆持久化服务（SQLite）。
/// 存储用户的投资偏好、历史分析结论、自定义标签等，供 AI 上下文使用。
/// 采用有界设计：条目数上限 <see cref="MaxEntryCount"/>，总字符上限 <see cref="MaxTotalChars"/>。
/// </summary>
public class UserMemoryService : SqliteServiceBase
{
    public const int MaxEntryCount = 50;
    public const int MaxTotalChars = 5000;

    public UserMemoryService(ILogger<UserMemoryService> logger)
        : base("user_memory.db", logger)
    {
    }

    /// <summary>
    /// 保存一条记忆条目。如果容量已满，返回 false 并输出诊断信息。
    /// </summary>
    public async Task<(bool Success, string? Error)> SaveMemoryAsync(
        string category, string key, string value, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);

        var usage = await GetUsageAsync(ct);
        bool isReplace = await ExistsAsync(category, key, ct);

        if (!isReplace)
        {
            if (usage.EntryCount >= MaxEntryCount)
                return (false, $"记忆条目数已达上限 {usage.EntryCount}/{MaxEntryCount}，请先删除旧条目再添加。");

            if (usage.TotalChars + value.Length > MaxTotalChars)
                return (false, $"记忆总字符数将超过上限（当前 {usage.TotalChars}/{MaxTotalChars}），请先精简或删除旧条目。");
        }

        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_memories (category, key, value, priority, updated_at)
            VALUES (@category, @key, @value, 0, @updatedAt)
            ON CONFLICT(category, key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@category", category);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// 获取指定类别的所有记忆条目
    /// </summary>
    public async Task<Dictionary<string, string>> GetMemoriesAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM user_memories WHERE category = @category ORDER BY priority DESC, updated_at DESC";
        cmd.Parameters.AddWithValue("@category", category);

        var result = new Dictionary<string, string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }
        return result;
    }

    /// <summary>
    /// 获取所有记忆条目（用于注入 AI 上下文）
    /// </summary>
    public async Task<List<(string Category, string Key, string Value)>> GetAllMemoriesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT category, key, value FROM user_memories ORDER BY priority DESC, category, updated_at DESC";

        var result = new List<(string, string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }

    /// <summary>
    /// 获取高优先级记忆条目（用于 L1 层始终加载）
    /// </summary>
    public async Task<List<(string Category, string Key, string Value)>> GetHighPriorityMemoriesAsync(
        int minPriority = 1, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT category, key, value FROM user_memories
            WHERE priority >= @minPriority
            ORDER BY priority DESC, updated_at DESC
            """;
        cmd.Parameters.AddWithValue("@minPriority", minPriority);

        var result = new List<(string, string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }

    /// <summary>
    /// 设置记忆条目的优先级（0=普通，1+=高优先级，始终加载到上下文）
    /// </summary>
    public async Task SetPriorityAsync(string category, string key, int priority, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE user_memories SET priority = @priority WHERE category = @category AND key = @key";
        cmd.Parameters.AddWithValue("@priority", priority);
        cmd.Parameters.AddWithValue("@category", category);
        cmd.Parameters.AddWithValue("@key", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 删除一条记忆条目
    /// </summary>
    public async Task DeleteMemoryAsync(string category, string key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_memories WHERE category = @category AND key = @key";
        cmd.Parameters.AddWithValue("@category", category);
        cmd.Parameters.AddWithValue("@key", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 获取当前记忆用量统计
    /// </summary>
    public async Task<MemoryUsage> GetUsageAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(LENGTH(value)), 0) FROM user_memories";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new MemoryUsage
        {
            EntryCount = reader.GetInt32(0),
            TotalChars = reader.GetInt32(1),
            MaxEntryCount = MaxEntryCount,
            MaxTotalChars = MaxTotalChars
        };
    }

    private async Task<bool> ExistsAsync(string category, string key, CancellationToken ct)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM user_memories WHERE category = @category AND key = @key LIMIT 1";
        cmd.Parameters.AddWithValue("@category", category);
        cmd.Parameters.AddWithValue("@key", key);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    protected override async Task InitializeDatabaseAsync()
    {
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS user_memories (
                    category TEXT NOT NULL,
                    key TEXT NOT NULL,
                    value TEXT NOT NULL,
                    priority INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (category, key)
                );
                CREATE INDEX IF NOT EXISTS idx_memories_category ON user_memories(category);
                CREATE INDEX IF NOT EXISTS idx_memories_priority ON user_memories(priority);
                """;
            await cmd.ExecuteNonQueryAsync();
            Logger.LogInformation("用户记忆数据库初始化完成");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "初始化用户记忆数据库失败");
            throw;
        }
    }
}

/// <summary>
/// 记忆用量统计
/// </summary>
public record MemoryUsage
{
    public int EntryCount { get; init; }
    public int TotalChars { get; init; }
    public int MaxEntryCount { get; init; }
    public int MaxTotalChars { get; init; }
}
