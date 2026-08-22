using System.Text.Json;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services;

/// <summary>
/// 聊天会话持久化服务，支持将对话历史保存到 SQLite 并在应用重启后恢复。
/// </summary>
public class ChatSessionPersistenceService : SqliteServiceBase
{
    public ChatSessionPersistenceService(ILogger<ChatSessionPersistenceService> logger)
        : base(logger)
    {
    }

    /// <summary>
    /// 保存聊天会话
    /// </summary>
    public async Task SaveSessionAsync(ChatSessionSnapshot snapshot, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO chat_sessions (
                id, stock_code, title, messages_json, analysis_context,
                agent_session_json, session_schema_version, provider_id, model_id, endpoint,
                runtime_configuration_fingerprint, created_at, updated_at)
            VALUES (
                @id, @stockCode, @title, @messagesJson, @analysisContext,
                @agentSessionJson, @sessionSchemaVersion, @providerId, @modelId, @endpoint,
                @runtimeConfigurationFingerprint, @createdAt, @updatedAt)
            """;
        cmd.Parameters.AddWithValue("@id", snapshot.Id);
        cmd.Parameters.AddWithValue("@stockCode", snapshot.StockCode);
        cmd.Parameters.AddWithValue("@title", snapshot.Title);
        cmd.Parameters.AddWithValue("@messagesJson", JsonSerializer.Serialize(snapshot.Messages));
        cmd.Parameters.AddWithValue("@analysisContext", (object?)snapshot.AnalysisContext ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "@agentSessionJson",
            snapshot.AgentSessionState is { } state ? state.GetRawText() : DBNull.Value);
        cmd.Parameters.AddWithValue("@sessionSchemaVersion", snapshot.SessionSchemaVersion);
        cmd.Parameters.AddWithValue("@providerId", snapshot.ProviderId);
        cmd.Parameters.AddWithValue("@modelId", snapshot.ModelId);
        cmd.Parameters.AddWithValue("@endpoint", snapshot.Endpoint);
        cmd.Parameters.AddWithValue(
            "@runtimeConfigurationFingerprint",
            snapshot.RuntimeConfigurationFingerprint);
        cmd.Parameters.AddWithValue("@createdAt", snapshot.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        await UpdateFtsIndexAsync(conn, snapshot);
    }

    /// <summary>
    /// 加载指定会话
    /// </summary>
    public async Task<ChatSessionSnapshot?> LoadSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);

        await using var ftsCmd = conn.CreateCommand();
        ftsCmd.CommandText = "DELETE FROM chat_messages_fts WHERE session_id = @id";
        ftsCmd.Parameters.AddWithValue("@id", sessionId);
        await ftsCmd.ExecuteNonQueryAsync(ct);

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
        var agentSessionOrd = reader.GetOrdinal("agent_session_json");
        var agentSessionJson = reader.IsDBNull(agentSessionOrd)
            ? null
            : reader.GetString(agentSessionOrd);

        return new ChatSessionSnapshot
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            StockCode = reader.GetString(reader.GetOrdinal("stock_code")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Messages = messages,
            AnalysisContext = reader.IsDBNull(contextOrd) ? null : reader.GetString(contextOrd),
            AgentSessionState = string.IsNullOrWhiteSpace(agentSessionJson)
                ? null
                : JsonDocument.Parse(agentSessionJson).RootElement.Clone(),
            SessionSchemaVersion = reader.GetInt32(reader.GetOrdinal("session_schema_version")),
            ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
            ModelId = reader.GetString(reader.GetOrdinal("model_id")),
            Endpoint = reader.GetString(reader.GetOrdinal("endpoint")),
            RuntimeConfigurationFingerprint = reader.GetString(
                reader.GetOrdinal("runtime_configuration_fingerprint")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
        };
    }

    /// <summary>
    /// 全文搜索历史对话消息
    /// </summary>
    public async Task<List<SessionSearchResult>> SearchSessionsAsync(
        string query, int limit = 10, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        if (string.IsNullOrWhiteSpace(query)) return [];

        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.session_id, f.role, f.content, f.author_name, f.message_index,
                   s.stock_code, s.title, s.updated_at
            FROM chat_messages_fts f
            JOIN chat_sessions s ON s.id = f.session_id
            WHERE chat_messages_fts MATCH @query
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<SessionSearchResult>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new SessionSearchResult
                {
                    SessionId = reader.GetString(0),
                    Role = reader.GetString(1),
                    Content = reader.GetString(2),
                    AuthorName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    StockCode = reader.GetString(5),
                    SessionTitle = reader.GetString(6),
                    UpdatedAt = DateTime.Parse(reader.GetString(7))
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "FTS5 搜索失败，查询: {Query}", query);
        }
        return results;
    }

    /// <summary>
    /// 保存会话时同步更新 FTS5 索引
    /// </summary>
    private async Task UpdateFtsIndexAsync(SqliteConnection conn, ChatSessionSnapshot snapshot)
    {
        // 先删除旧索引
        await using (var delCmd = conn.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM chat_messages_fts WHERE session_id = @id";
            delCmd.Parameters.AddWithValue("@id", snapshot.Id);
            await delCmd.ExecuteNonQueryAsync();
        }

        // 逐条插入消息到 FTS 索引
        for (int i = 0; i < snapshot.Messages.Count; i++)
        {
            var msg = snapshot.Messages[i];
            if (string.IsNullOrWhiteSpace(msg.Content)) continue;

            await using var insCmd = conn.CreateCommand();
            insCmd.CommandText = """
                INSERT INTO chat_messages_fts (session_id, role, content, author_name, message_index)
                VALUES (@sid, @role, @content, @author, @idx)
                """;
            insCmd.Parameters.AddWithValue("@sid", snapshot.Id);
            insCmd.Parameters.AddWithValue("@role", msg.Role);
            insCmd.Parameters.AddWithValue("@content", msg.Content);
            insCmd.Parameters.AddWithValue("@author", (object?)msg.AuthorName ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("@idx", i);
            await insCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureSessionColumnsAsync(SqliteConnection conn)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragmaCommand = conn.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA table_info(chat_sessions)";
            await using var reader = await pragmaCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingColumns.Add(reader.GetString(1));
        }

        var requiredColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["agent_session_json"] = "TEXT",
            ["session_schema_version"] = "INTEGER NOT NULL DEFAULT 0",
            ["provider_id"] = "TEXT NOT NULL DEFAULT ''",
            ["model_id"] = "TEXT NOT NULL DEFAULT ''",
            ["endpoint"] = "TEXT NOT NULL DEFAULT ''",
            ["runtime_configuration_fingerprint"] = "TEXT NOT NULL DEFAULT ''"
        };

        foreach (var (columnName, definition) in requiredColumns)
        {
            if (existingColumns.Contains(columnName))
                continue;

            await using var alterCommand = conn.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE chat_sessions ADD COLUMN {columnName} {definition}";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }

    protected override async Task InitializeDatabaseAsync()
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
                    agent_session_json TEXT,
                    session_schema_version INTEGER NOT NULL DEFAULT 0,
                    provider_id TEXT NOT NULL DEFAULT '',
                    model_id TEXT NOT NULL DEFAULT '',
                    endpoint TEXT NOT NULL DEFAULT '',
                    runtime_configuration_fingerprint TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_chat_sessions_stock ON chat_sessions(stock_code);
                CREATE INDEX IF NOT EXISTS idx_chat_sessions_updated ON chat_sessions(updated_at);

                CREATE VIRTUAL TABLE IF NOT EXISTS chat_messages_fts USING fts5(
                    session_id UNINDEXED,
                    role UNINDEXED,
                    content,
                    author_name UNINDEXED,
                    message_index UNINDEXED,
                    tokenize='unicode61'
                );
                """;
            await cmd.ExecuteNonQueryAsync();
            await EnsureSessionColumnsAsync(conn);
            Logger.LogInformation("聊天会话数据库初始化完成（含 FTS5 索引与 MAF Session 状态）");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "初始化聊天会话数据库失败");
            throw;
        }
    }
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
    public JsonElement? AgentSessionState { get; set; }
    public int SessionSchemaVersion { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string RuntimeConfigurationFingerprint { get; set; } = string.Empty;
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

/// <summary>
/// 全文搜索结果条目
/// </summary>
public class SessionSearchResult
{
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public string SessionTitle { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
