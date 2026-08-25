using MarketAssistant.Rag.Interfaces;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 基于 SQLite 旁路表的文档段落清单实现（P1-01）。
/// 表结构：rag_document_catalog(collection, document_id, document_uri, content_hash, keys_json, embedding_model_id, dimension, updated_at)
/// </summary>
public sealed class SqliteRagDocumentCatalog : IRagDocumentCatalog
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqliteRagDocumentCatalog(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("清单数据库路径不能为空", nameof(dbPath));

        _dbPath = dbPath;
    }

    public async Task<IReadOnlyList<string>> GetKeysAsync(
        string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT keys_json FROM rag_document_catalog WHERE collection = $c AND document_id = $d";
        cmd.Parameters.AddWithValue("$c", collectionName);
        cmd.Parameters.AddWithValue("$d", documentId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not string json || string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    public async Task ReplaceAsync(RagDocumentCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rag_document_catalog(collection, document_id, document_uri, content_hash, keys_json, embedding_model_id, dimension, updated_at)
            VALUES ($c, $d, $u, $h, $k, $m, $dim, $t)
            ON CONFLICT(collection, document_id) DO UPDATE SET
                document_uri = $u, content_hash = $h, keys_json = $k,
                embedding_model_id = $m, dimension = $dim, updated_at = $t
            """;
        cmd.Parameters.AddWithValue("$c", entry.CollectionName);
        cmd.Parameters.AddWithValue("$d", entry.DocumentId);
        cmd.Parameters.AddWithValue("$u", entry.DocumentUri);
        cmd.Parameters.AddWithValue("$h", entry.ContentHash);
        cmd.Parameters.AddWithValue("$k", JsonSerializer.Serialize(entry.Keys));
        cmd.Parameters.AddWithValue("$m", entry.EmbeddingModelId);
        cmd.Parameters.AddWithValue("$dim", entry.Dimension);
        cmd.Parameters.AddWithValue("$t", entry.UpdatedAt.UtcTicks);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM rag_document_catalog WHERE collection = $c AND document_id = $d";
        cmd.Parameters.AddWithValue("$c", collectionName);
        cmd.Parameters.AddWithValue("$d", documentId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

            await using var conn = CreateConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS rag_document_catalog (
                    collection TEXT NOT NULL,
                    document_id TEXT NOT NULL,
                    document_uri TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    keys_json TEXT NOT NULL,
                    embedding_model_id TEXT NOT NULL,
                    dimension INTEGER NOT NULL,
                    updated_at INTEGER NOT NULL,
                    PRIMARY KEY (collection, document_id)
                )
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}