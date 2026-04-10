using System.Text.Json;
using MarketAssistant.Applications.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services;

/// <summary>
/// 聊天会话持久化服务，支持将对话历史保存到 SQLite 并在应用重启后恢复。
/// </summary>
public class ChatSessionPersistenceService : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<ChatSessionPersistenceService> _logger;
    private readonly Task _initializeTask;

    public ChatSessionPersistenceService(ILogger<ChatSessionPersistenceService> logger)
    {
        _logger = logger;

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName);
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "chat_sessions.db");
        _connectionString = $"Data Source={dbPath}";
        _initializeTask = InitializeDatabaseAsync();
    }

    /// <summary>
    /// 保存聊天会话
    /// </summary>
    public async Task SaveSessionAsync(ChatSessionSnapshot snapshot, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO chat_sessions (id, stock_code, title, messages_json, analysis_context, created_at, updated_at)
            VALUES (@id, @stockCode, @title, @messagesJson, @analysisContext, @createdAt, @updatedAt)
            """;
        cmd.Parameters.AddWithValue("@id", snapshot.Id);
        cmd.Parameters.AddWithValue("@stockCode", snapshot.StockCode);
        cmd.Parameters.AddWithValue("@title", snapshot.Title);
        cmd.Parameters.AddWithValue("@messagesJson", JsonSerializer.Serialize(snapshot.Messages));
        cmd.Parameters.AddWithValue("@analysisContext", (object?)snapshot.AnalysisContext ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", snapshot.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 加载指定会话
    /// </summary>
    public async Task<ChatSessionSnapshot?> LoadSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM chat_sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadSnapshot(reader);
    }

    /// <summary>
    /// 获取所有会话摘要（不含消息体）
    /// </summary>
    public async Task<List<ChatSessionSummary>> GetSessionSummariesAsync(CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, stock_code, title, created_at, updated_at
            FROM chat_sessions
            ORDER BY updated_at DESC
            LIMIT 50
            """;

        var summaries = new List<ChatSessionSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            summaries.Add(new ChatSessionSummary
            {
                Id = reader.GetString(0),
                StockCode = reader.GetString(1),
                Title = reader.GetString(2),
                CreatedAt = DateTime.Parse(reader.GetString(3)),
                UpdatedAt = DateTime.Parse(reader.GetString(4))
            });
        }
        return summaries;
    }

    /// <summary>
    /// 删除指定会话
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM chat_sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ChatSessionSnapshot ReadSnapshot(SqliteDataReader reader)
    {
        var messagesJson = reader.GetString(reader.GetOrdinal("messages_json"));
        var messages = JsonSerializer.Deserialize<List<ChatMessageDto>>(messagesJson) ?? [];

        var contextOrd = reader.GetOrdinal("analysis_context");

        return new ChatSessionSnapshot
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            StockCode = reader.GetString(reader.GetOrdinal("stock_code")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Messages = messages,
            AnalysisContext = reader.IsDBNull(contextOrd) ? null : reader.GetString(contextOrd),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
        };
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS chat_sessions (
                    id TEXT PRIMARY KEY,
                    stock_code TEXT NOT NULL,
                    title TEXT NOT NULL,
                    messages_json TEXT NOT NULL,
                    analysis_context TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_sessions_stock ON chat_sessions(stock_code);
                CREATE INDEX IF NOT EXISTS idx_sessions_updated ON chat_sessions(updated_at);
                """;
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("聊天会话数据库初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化聊天会话数据库失败");
        }
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// 聊天会话快照（用于持久化）
/// </summary>
public class ChatSessionSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StockCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<ChatMessageDto> Messages { get; set; } = [];
    public string? AnalysisContext { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 聊天消息 DTO（用于序列化）
/// </summary>
public class ChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
}

/// <summary>
/// 聊天会话摘要（列表展示用）
/// </summary>
public class ChatSessionSummary
{
    public string Id { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
