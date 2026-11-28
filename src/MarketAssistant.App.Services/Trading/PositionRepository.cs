using System.Globalization;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 持仓 FIFO 追踪仓储（internal）：开仓、平仓、未平仓持仓查询与加权平均开仓价计算。
/// </summary>
internal sealed class PositionRepository(
    TradingSchemaInitializer schema,
    TradingEnvironmentService environment,
    Microsoft.Extensions.Logging.ILogger logger) : TradingRepositoryBase(schema, environment, logger)
{

    /// <summary>
    /// 开仓：插入一条新的持仓记录
    /// </summary>
    public async Task OpenPositionAsync(Position position, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        using var tx = conn.BeginTransaction();
        await OpenPositionAsync(position, tx, ct);
        tx.Commit();
    }

    internal async Task OpenPositionAsync(Position position, SqliteTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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
        using var tx = conn.BeginTransaction();
        var pnl = await ClosePositionFifoAsync(symbol, closeQty, closePrice, side, tx, ct);
        tx.Commit();
        return pnl;
    }

    internal async Task<decimal> ClosePositionFifoAsync(string symbol, decimal closeQty, decimal closePrice,
        PositionSide side, SqliteTransaction tx, CancellationToken ct, string? gridStrategyId = null)
    {
        var conn = tx.Connection!;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
                SELECT id, quantity, entry_price, closed_quantity
                FROM positions
                WHERE environment = @environment AND symbol = @symbol AND side = @side AND (quantity - closed_quantity) > 0
                  AND (@strategy IS NULL OR strategy_id = @strategy OR NOT EXISTS
                      (SELECT 1 FROM strategies WHERE id = @strategy AND type IN (@gridType, @aiType)))
                ORDER BY opened_at ASC
                """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@symbol", symbol);
        cmd.Parameters.AddWithValue("@side", (int)side);
        cmd.Parameters.AddWithValue("@strategy", (object?)gridStrategyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gridType", (int)StrategyType.GridTrading);
        cmd.Parameters.AddWithValue("@aiType", (int)StrategyType.AISignal);

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

        return realizedPnl;
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

    /// <summary>
    /// 按交易所实际持仓校准本地账本：把某标的某方向的未平量直接改写为交易所的未平量。
    ///
    /// 用于 <c>PositionReconciliationService</c>——外部手动成交、漏单、断线期间成交都会让
    /// 本地 FIFO 与交易所产生漂移，而本地账本是 DCA 均价、PnL 与风控可用量的计算基础。
    /// 这里不重算 PnL（历史盈亏已在当时的成交里结算），只对齐"还剩多少仓位、均价多少"。
    /// </summary>
    /// <param name="targetRemainingQty">交易所的未平量（绝对值）。为 0 时把本地记录全部平掉。</param>
    /// <param name="exchangeEntryPrice">交易所的开仓均价；&lt;= 0 时保留本地均价。</param>
    public async Task ReconcileOpenPositionAsync(
        string symbol, PositionSide side, decimal targetRemainingQty, decimal exchangeEntryPrice,
        CancellationToken ct = default)
    {
        if (targetRemainingQty < 0)
            targetRemainingQty = 0;

        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await using var selectCmd = conn.CreateCommand();
            selectCmd.Transaction = (SqliteTransaction)tx;
            selectCmd.CommandText = """
                SELECT id, quantity, closed_quantity
                FROM positions
                WHERE environment = @environment AND symbol = @symbol AND side = @side AND (quantity - closed_quantity) > 0
                ORDER BY opened_at ASC
                """;
            selectCmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
            selectCmd.Parameters.AddWithValue("@symbol", symbol);
            selectCmd.Parameters.AddWithValue("@side", (int)side);

            var rows = new List<(string Id, decimal Remaining)>();
            await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetString(0);
                    var qty = ReadDecimal(reader, 1);
                    var closed = ReadDecimal(reader, 2);
                    rows.Add((id, qty - closed));
                }
            }

            // 交易所已无仓位：本地记录全部标记为已平（保留历史行，不删除）
            if (targetRemainingQty <= 0)
            {
                foreach (var (id, remaining) in rows)
                    await SetClosedQuantityAsync(conn, (SqliteTransaction)tx, id, remaining, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            var localTotal = rows.Sum(row => row.Remaining);

            if (localTotal <= 0)
            {
                // 本地无记录但交易所仍有仓位（外部开仓）：补一条账本行，使风控与均价有据可依
                await using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = (SqliteTransaction)tx;
                insertCmd.CommandText = """
                    INSERT INTO positions (id, environment, symbol, side, quantity, entry_price, closed_quantity, strategy_id, opened_at)
                    VALUES (@id, @environment, @symbol, @side, @qty, @entry, 0, NULL, @openedAt)
                    """;
                insertCmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                insertCmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
                insertCmd.Parameters.AddWithValue("@symbol", symbol);
                insertCmd.Parameters.AddWithValue("@side", (int)side);
                insertCmd.Parameters.AddWithValue("@qty", ToDb(targetRemainingQty));
                insertCmd.Parameters.AddWithValue("@entry", ToDb(exchangeEntryPrice > 0 ? exchangeEntryPrice : 0));
                insertCmd.Parameters.AddWithValue("@openedAt", DateTime.UtcNow.ToString("O"));
                await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            // 按 FIFO 调整未平量：少了就从最早的行平掉差额，多了就补到最新一行
            var delta = targetRemainingQty - localTotal;
            if (delta < 0)
            {
                var toClose = -delta;
                foreach (var (id, remaining) in rows)
                {
                    if (toClose <= 0)
                        break;

                    var closeThis = Math.Min(toClose, remaining);
                    await SetClosedQuantityAsync(conn, (SqliteTransaction)tx, id, closeThis, ct, addToExisting: true)
                        .ConfigureAwait(false);
                    toClose -= closeThis;
                }
            }
            else if (delta > 0)
            {
                await using var addCmd = conn.CreateCommand();
                addCmd.Transaction = (SqliteTransaction)tx;
                addCmd.CommandText = """
                    INSERT INTO positions (id, environment, symbol, side, quantity, entry_price, closed_quantity, strategy_id, opened_at)
                    VALUES (@id, @environment, @symbol, @side, @qty, @entry, 0, NULL, @openedAt)
                    """;
                addCmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                addCmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
                addCmd.Parameters.AddWithValue("@symbol", symbol);
                addCmd.Parameters.AddWithValue("@side", (int)side);
                addCmd.Parameters.AddWithValue("@qty", ToDb(delta));
                addCmd.Parameters.AddWithValue("@entry", ToDb(exchangeEntryPrice > 0 ? exchangeEntryPrice : 0));
                addCmd.Parameters.AddWithValue("@openedAt", DateTime.UtcNow.ToString("O"));
                await addCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task SetClosedQuantityAsync(
        SqliteConnection conn, SqliteTransaction tx, string positionId, decimal closeQty,
        CancellationToken ct, bool addToExisting = false)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = addToExisting
            ? "UPDATE positions SET closed_quantity = closed_quantity + @close WHERE id = @id"
            : "UPDATE positions SET closed_quantity = quantity WHERE id = @id";
        if (addToExisting)
            cmd.Parameters.AddWithValue("@close", ToDb(closeQty));
        cmd.Parameters.AddWithValue("@id", positionId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
