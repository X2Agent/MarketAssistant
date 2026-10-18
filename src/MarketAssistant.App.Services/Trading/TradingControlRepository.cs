using System.Globalization;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易紧急停止状态仓储（internal）：全局单例行的停机标记读写。
/// 与 <see cref="TradingEnvironmentService"/> 的环境 key 无关——停机是全局状态，
/// 不随交易模式切换失效（避免"切环境即自动恢复交易"的意外）。
/// </summary>
internal sealed class TradingControlRepository(
    TradingSchemaInitializer schema,
    TradingEnvironmentService environment,
    Microsoft.Extensions.Logging.ILogger logger) : TradingRepositoryBase(schema, environment, logger)
{
    /// <summary>单例行主键。</summary>
    private const int SingletonId = 1;

    public async Task<(bool Halted, string Reason, DateTime? HaltedAtUtc)> LoadAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT halted, reason, halted_at FROM trading_control WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", SingletonId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return (false, string.Empty, null);

        DateTime? haltedAt = null;
        if (!reader.IsDBNull(2)
            && DateTime.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            haltedAt = parsed;
        }

        return (reader.GetInt32(0) != 0, reader.IsDBNull(1) ? string.Empty : reader.GetString(1), haltedAt);
    }

    public async Task HaltAsync(string reason, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trading_control (id, halted, reason, halted_at)
            VALUES (@id, 1, @reason, @haltedAt)
            ON CONFLICT(id) DO UPDATE SET halted = 1, reason = @reason, halted_at = @haltedAt
            """;
        cmd.Parameters.AddWithValue("@id", SingletonId);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@haltedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ResumeAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trading_control (id, halted, reason, halted_at)
            VALUES (@id, 0, '', NULL)
            ON CONFLICT(id) DO UPDATE SET halted = 0, reason = '', halted_at = NULL
            """;
        cmd.Parameters.AddWithValue("@id", SingletonId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
