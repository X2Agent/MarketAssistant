using System.Globalization;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 持仓 FIFO 追踪仓储（internal）：开仓、平仓、未平仓持仓查询与加权平均开仓价计算。
/// </summary>
internal sealed class PositionRepository : TradingRepositoryBase
{
    public PositionRepository(
        TradingSchemaInitializer schema,
        TradingEnvironmentService environment,
        Microsoft.Extensions.Logging.ILogger logger)
        : base(schema, environment, logger)
    {
    }

    /// <summary>
    /// 开仓：插入一条新的持仓记录
    /// </summary>
    public async Task OpenPositionAsync(Position position, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO positions (id, environment, symbol, side, quantity, entry_price, closed_quantity, strategy_id, opened_at)
            VALUES (@id, @environment, @symbol, @side, @qty, @entry, 0, @stratId, @openedAt)
            """;
        cmd.Parameters.AddWithValue("@id", position.Id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@symbol", position.Symbol);
        cmd.Parameters.AddWithValue("@side", (int)position.Side);
        cmd.Parameters.AddWithValue("@qty", ToDb(position.Quantity));
        cmd.Parameters.AddWithValue("@entry", ToDb(position.EntryPrice));
        cmd.Parameters.AddWithValue("@stratId", (object?)position.StrategyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@openedAt", position.OpenedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 平仓：按 FIFO 顺序匹配指定方向的持仓，更新 closed_quantity，返回已实现盈亏。
    /// 多头盈亏 = (平仓价 - 开仓价) × 数量；空头盈亏 = (开仓价 - 平仓价) × 数量。
    /// </summary>
    public async Task<decimal> ClosePositionFifoAsync(
        string symbol, decimal closeQty, decimal closePrice,
        CancellationToken ct = default, PositionSide side = PositionSide.Long)
    {
        if (closeQty <= 0)
            return 0;

        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                SELECT id, quantity, entry_price, closed_quantity
                FROM positions
                WHERE environment = @environment AND symbol = @symbol AND side = @side AND (quantity - closed_quantity) > 0
                ORDER BY opened_at ASC
                """;
            cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
            cmd.Parameters.AddWithValue("@symbol", symbol);
            cmd.Parameters.AddWithValue("@side", (int)side);

            var toClose = new List<(string id, decimal available, decimal entryPrice)>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetString(0);
                    var qty = ReadDecimal(reader, 1);
                    var entry = ReadDecimal(reader, 2);
                    var closed = ReadDecimal(reader, 3);
                    toClose.Add((id, qty - closed, entry));
                }
            }

            decimal realizedPnl = 0;
            var remaining = closeQty;

            foreach (var (id, available, entry) in toClose)
            {
                if (remaining <= 0)
                    break;

                var closeThis = Math.Min(remaining, available);
                // 空头平仓时开仓价高于平仓价才盈利，与多头相反
                realizedPnl += side == PositionSide.Long
                    ? (closePrice - entry) * closeThis
                    : (entry - closePrice) * closeThis;

                await using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = (SqliteTransaction)tx;
                updateCmd.CommandText = """
                    UPDATE positions SET closed_quantity = closed_quantity + @close
                    WHERE id = @id
                    """;
                updateCmd.Parameters.AddWithValue("@close", ToDb(closeThis));
                updateCmd.Parameters.AddWithValue("@id", id);
                await updateCmd.ExecuteNonQueryAsync(ct);

                remaining -= closeThis;
            }

            await tx.CommitAsync(ct);
            return realizedPnl;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 获取指定 symbol 的当前未平仓多头持仓（用于 UI 展示与风控）
    /// </summary>
    public async Task<List<Position>> GetOpenPositionsAsync(string? symbol = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (string.IsNullOrEmpty(symbol))
        {
            cmd.CommandText = """
                SELECT id, symbol, side, quantity, entry_price, closed_quantity, strategy_id, opened_at
                FROM positions
                WHERE environment = @environment AND (quantity - closed_quantity) > 0
                ORDER BY opened_at ASC
                """;
            cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        }
        else
        {
            cmd.CommandText = """
                SELECT id, symbol, side, quantity, entry_price, closed_quantity, strategy_id, opened_at
                FROM positions
                WHERE environment = @environment AND symbol = @symbol AND (quantity - closed_quantity) > 0
                ORDER BY opened_at ASC
                """;
            cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
            cmd.Parameters.AddWithValue("@symbol", symbol);
        }

        var positions = new List<Position>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            positions.Add(ReadPosition(reader));
        return positions;
    }

    /// <summary>
    /// 计算指定 symbol 的加权平均开仓价（仅未平仓部分，用于风控与 UI）
    /// </summary>
    public async Task<decimal> GetOpenPositionAvgEntryPriceAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CASE WHEN SUM(quantity - closed_quantity) > 0
                THEN SUM((quantity - closed_quantity) * entry_price) / SUM(quantity - closed_quantity)
                ELSE 0 END
            FROM positions
            WHERE environment = @environment AND symbol = @symbol AND side = @side AND (quantity - closed_quantity) > 0
            """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@symbol", symbol);
        cmd.Parameters.AddWithValue("@side", (int)PositionSide.Long);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is double d)
            return (decimal)d;
        return 0;
    }

    private static Position ReadPosition(SqliteDataReader reader)
    {
        var position = new Position
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Symbol = reader.GetString(reader.GetOrdinal("symbol")),
            Side = (PositionSide)reader.GetInt32(reader.GetOrdinal("side")),
            Quantity = ReadDecimal(reader, reader.GetOrdinal("quantity")),
            EntryPrice = ReadDecimal(reader, reader.GetOrdinal("entry_price")),
            ClosedQuantity = ReadDecimal(reader, reader.GetOrdinal("closed_quantity")),
            OpenedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("opened_at")), CultureInfo.InvariantCulture)
        };

        var sidOrd = reader.GetOrdinal("strategy_id");
        if (!reader.IsDBNull(sidOrd))
            position.StrategyId = reader.GetString(sidOrd);

        return position;
    }
}
