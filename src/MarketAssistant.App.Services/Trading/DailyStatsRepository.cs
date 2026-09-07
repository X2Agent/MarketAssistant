using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 日统计仓储（internal）：今日交易统计的查询与累计更新。
/// </summary>
internal sealed class DailyStatsRepository : TradingRepositoryBase
{
    public DailyStatsRepository(
        TradingSchemaInitializer schema,
        TradingEnvironmentService environment,
        Microsoft.Extensions.Logging.ILogger logger)
        : base(schema, environment, logger)
    {
    }

    public async Task<DailyStats> GetTodayStatsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        var today = GetTodayDateString();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM daily_stats WHERE environment = @environment AND date = @date";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@date", today);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return new DailyStats
            {
                Date = reader.GetString(reader.GetOrdinal("date")),
                TradeCount = reader.GetInt32(reader.GetOrdinal("trade_count")),
                TotalPnl = ReadDecimal(reader, reader.GetOrdinal("total_pnl")),
                TotalCommission = ReadDecimal(reader, reader.GetOrdinal("total_commission"))
            };
        }

        return new DailyStats { Date = today };
    }

    /// <summary>
    /// 更新今日统计：累计已实现盈亏与手续费，并按需增加交易次数。
    /// <paramref name="countTrade"/> 仅在订单首次实际成交（executed_qty 从 0 变为 >0）时为 true，
    /// 避免未成交订单被计数、以及下单与对账重复计数。
    /// </summary>
    public async Task UpdateDailyStatsAsync(decimal pnl, decimal commission, bool countTrade = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        var today = GetTodayDateString();
        await using var conn = await OpenConnectionAsync(ct);

        // 金额列以 TEXT 精确存储，SQL 数值加法会把 TEXT 退化为 double，
        // 因此在 C# 侧完成累加后整体写回，保持十进制精度。
        var tradeCount = countTrade ? 1 : 0;
        var totalPnl = pnl;
        var totalCommission = commission;

        await using (var selectCmd = conn.CreateCommand())
        {
            selectCmd.CommandText = "SELECT trade_count, total_pnl, total_commission FROM daily_stats WHERE environment = @environment AND date = @date";
            selectCmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
            selectCmd.Parameters.AddWithValue("@date", today);
            await using var reader = await selectCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                tradeCount += reader.GetInt32(0);
                totalPnl += ReadDecimal(reader, 1);
                totalCommission += ReadDecimal(reader, 2);
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO daily_stats (environment, date, trade_count, total_pnl, total_commission)
            VALUES (@environment, @date, @tradeCount, @pnl, @comm)
            ON CONFLICT(environment, date) DO UPDATE SET
                trade_count = @tradeCount,
                total_pnl = @pnl,
                total_commission = @comm
            """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@date", today);
        cmd.Parameters.AddWithValue("@tradeCount", tradeCount);
        cmd.Parameters.AddWithValue("@pnl", ToDb(totalPnl));
        cmd.Parameters.AddWithValue("@comm", ToDb(totalCommission));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
