namespace MarketAssistant.Services.Trading;

/// <summary>
/// 账户快照仓储（internal）：每日账户价值快照的保存与历史峰值查询（用于回撤计算）。
/// </summary>
internal sealed class AccountSnapshotRepository : TradingRepositoryBase
{
    public AccountSnapshotRepository(
        TradingSchemaInitializer schema,
        TradingEnvironmentService environment,
        Microsoft.Extensions.Logging.ILogger logger)
        : base(schema, environment, logger)
    {
    }

    /// <summary>
    /// 保存每日账户快照（用于计算最大回撤）
    /// </summary>
    public async Task SaveAccountSnapshotAsync(decimal totalValueUsdt, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        var today = GetTodayDateString();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO account_snapshots (environment, date, total_value_usdt, snapshot_at)
            VALUES (@environment, @date, @value, @snapshotAt)
            ON CONFLICT(environment, date) DO UPDATE SET
                total_value_usdt = @value,
                snapshot_at = @snapshotAt
            """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@date", today);
        cmd.Parameters.AddWithValue("@value", ToDb(totalValueUsdt));
        cmd.Parameters.AddWithValue("@snapshotAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取历史最高账户价值（用于计算回撤），支持时间窗口下限。
    /// </summary>
    /// <param name="since">仅统计 snapshot_at >= 该时刻的快照；传 null 时统计全部历史。</param>
    public async Task<decimal> GetPeakAccountValueAsync(DateTime? since = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // 金额列以 TEXT 存储，MAX 会按字符串比较，需 CAST 为数值后再取最大值；
        // snapshot_at 统一为 "O" 格式 UTC 字符串，字典序与时间序一致，可直接比较
        var where = since.HasValue ? " AND snapshot_at >= @since" : "";
        cmd.CommandText = $"SELECT MAX(CAST(total_value_usdt AS REAL)) FROM account_snapshots WHERE environment = @environment{where}";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        if (since.HasValue)
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is double d)
            return (decimal)d;
        return 0;
    }
}
