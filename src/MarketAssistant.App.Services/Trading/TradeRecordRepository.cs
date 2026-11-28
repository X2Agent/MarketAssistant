using System.Globalization;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易记录仓储（internal）：交易记录的持久化、查询与对账。
/// 对账需联动持仓（FIFO）与日统计，故依赖 <see cref="PositionRepository"/> 与 <see cref="DailyStatsRepository"/>。
/// </summary>
internal sealed class TradeRecordRepository(
    TradingSchemaInitializer schema,
    TradingEnvironmentService environment,
    Microsoft.Extensions.Logging.ILogger logger,
    PositionRepository positions,
    DailyStatsRepository dailyStats) : TradingRepositoryBase(schema, environment, logger)
{
    private readonly PositionRepository _positions = positions;
    private readonly DailyStatsRepository _dailyStats = dailyStats;

    public async Task SaveTradeRecordAsync(TradeRecord record, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trade_records
                (id, environment, strategy_id, symbol, side, order_type, requested_qty, executed_qty,
                 requested_price, executed_price, commission, commission_asset, status,
                 binance_order_id, ai_reasoning, created_at, completed_at, slippage_percent, realized_pnl, reduce_only)
            VALUES
                (@id, @environment, @stratId, @symbol, @side, @orderType, @reqQty, @execQty,
                 @reqPrice, @execPrice, @commission, @commAsset, @status,
                 @binanceId, @aiReasoning, @createdAt, @completedAt, @slippagePercent, @realizedPnl, @reduceOnly)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@stratId", record.StrategyId);
        cmd.Parameters.AddWithValue("@symbol", record.Symbol);
        cmd.Parameters.AddWithValue("@side", (int)record.Side);
        cmd.Parameters.AddWithValue("@orderType", (int)record.OrderType);
        cmd.Parameters.AddWithValue("@reqQty", ToDb(record.RequestedQty));
        cmd.Parameters.AddWithValue("@execQty", ToDb(record.ExecutedQty));
        cmd.Parameters.AddWithValue("@reqPrice", ToDbNullable(record.RequestedPrice));
        cmd.Parameters.AddWithValue("@execPrice", ToDb(record.ExecutedPrice));
        cmd.Parameters.AddWithValue("@commission", ToDb(record.Commission));
        cmd.Parameters.AddWithValue("@commAsset", (object?)record.CommissionAsset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (int)record.Status);
        cmd.Parameters.AddWithValue("@binanceId", record.ExchangeOrderId);
        cmd.Parameters.AddWithValue("@reduceOnly", record.ReduceOnly);
        cmd.Parameters.AddWithValue("@aiReasoning", (object?)record.AIReasoning ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@completedAt", record.CompletedAt.HasValue ? (object)record.CompletedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@slippagePercent", ToDbNullable(record.SlippagePercent));
        cmd.Parameters.AddWithValue("@realizedPnl", ToDb(record.RealizedPnl));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<TradeRecord>> GetTradeRecordsAsync(
        string? symbol = null, DateTime? from = null, DateTime? to = null, int limit = 50,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var conditions = new List<string>
        {
            "environment = @environment"
        };
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);

        if (!string.IsNullOrEmpty(symbol))
        {
            conditions.Add("symbol = @symbol");
            cmd.Parameters.AddWithValue("@symbol", symbol);
        }
        if (from.HasValue)
        {
            conditions.Add("created_at >= @from");
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("O"));
        }
        if (to.HasValue)
        {
            conditions.Add("created_at <= @to");
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("O"));
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        cmd.CommandText = $"SELECT * FROM trade_records {where} ORDER BY created_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var records = new List<TradeRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            records.Add(ReadTradeRecord(reader));
        return records;
    }

    public async Task<List<TradeRecord>> GetRecordsByStrategyAsync(string strategyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM trade_records WHERE environment = @environment AND strategy_id = @stratId ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@stratId", strategyId);

        var records = new List<TradeRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            records.Add(ReadTradeRecord(reader));
        return records;
    }

    public async Task<List<TradeRecord>> GetUnsettledTradeRecordsAsync(
        string? symbol = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var conditions = new List<string>
        {
            "environment = @environment",
            "(status = @pending OR status = @partial)"
        };
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@pending", (int)TradeRecordStatus.Pending);
        cmd.Parameters.AddWithValue("@partial", (int)TradeRecordStatus.PartiallyFilled);

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            conditions.Add("symbol = @symbol");
            cmd.Parameters.AddWithValue("@symbol", symbol);
        }

        cmd.CommandText = $"SELECT * FROM trade_records WHERE {string.Join(" AND ", conditions)} ORDER BY created_at ASC";

        var records = new List<TradeRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            records.Add(ReadTradeRecord(reader));
        return records;
    }

    public async Task<TradeRecord> ReconcileTradeRecordAsync(
        TradeRecord existingRecord,
        ExchangeOrderResult latestOrder,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        using var tx = conn.BeginTransaction();
        await using (var lookup = conn.CreateCommand())
        {
            lookup.Transaction = tx;
            lookup.CommandText = "SELECT * FROM trade_records WHERE id = @id AND environment = @env";
            lookup.Parameters.AddWithValue("@id", existingRecord.Id);
            lookup.Parameters.AddWithValue("@env", CurrentEnvironmentKey);
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("订单记录不存在");
            existingRecord = ReadTradeRecord(reader);
        }
        if (latestOrder.ExecutedQty < existingRecord.ExecutedQty || (IsTerminalStatus(existingRecord.Status) &&
            (latestOrder.FillCommission <= existingRecord.Commission || !IsTerminalStatus(MapTradeRecordStatus(latestOrder.Status)))))
            return existingRecord;
        var latestStatus = MapTradeRecordStatus(latestOrder.Status);
        var latestExecutedQty = Math.Max(existingRecord.ExecutedQty, latestOrder.ExecutedQty);
        var latestExecutedPrice = CalculateEffectiveExecutedPrice(latestOrder, existingRecord.ExecutedPrice);
        var deltaExecutedQty = latestExecutedQty - existingRecord.ExecutedQty;
        var deltaCommission = latestOrder.FillCommission > existingRecord.Commission
            ? latestOrder.FillCommission - existingRecord.Commission
            : 0;
        if (deltaCommission > 0 && existingRecord.Commission > 0 && latestOrder.CommissionAsset != existingRecord.CommissionAsset)
            throw new InvalidOperationException("订单手续费资产发生变化，需要核对成交明细");
        DateTime? completedAt = IsTerminalStatus(latestStatus)
            ? existingRecord.CompletedAt ?? DateTime.UtcNow
            : null;

        var hasMeaningfulChange = deltaExecutedQty > 0
            || deltaCommission > 0
            || latestStatus != existingRecord.Status
            || latestExecutedPrice != existingRecord.ExecutedPrice
            || completedAt != existingRecord.CompletedAt;

        if (!hasMeaningfulChange && existingRecord.ExchangeOrderId != 0)
            return existingRecord;

        decimal realizedPnl = 0;
        if (deltaExecutedQty > 0)
        {
            var fillPrice = (latestExecutedQty * latestExecutedPrice - existingRecord.ExecutedQty * existingRecord.ExecutedPrice) / deltaExecutedQty;
            if (fillPrice <= 0) throw new InvalidOperationException("缺少可结算的增量成交金额");
            var buy = existingRecord.Side == OrderSide.Buy;
            if (!(existingRecord.ReduceOnly || (!IsFuturesMode && !buy)))
            {
                await _positions.OpenPositionAsync(new Position
                {
                    Symbol = existingRecord.Symbol,
                    Side = buy ? PositionSide.Long : PositionSide.Short,
                    Quantity = !IsFuturesMode && latestOrder.CommissionAsset == MarketAssistant.Infrastructure.Core.CryptoSymbolConverter.ExtractBaseCurrency(existingRecord.Symbol)
                        ? deltaExecutedQty - deltaCommission : deltaExecutedQty,
                    EntryPrice = fillPrice,
                    StrategyId = existingRecord.StrategyId,
                    OpenedAt = DateTime.UtcNow
                }, tx, ct).ConfigureAwait(false);
            }
            else
            {
                realizedPnl = await _positions.ClosePositionFifoAsync(
                    existingRecord.Symbol,
                    deltaExecutedQty,
                    fillPrice, buy ? PositionSide.Short : PositionSide.Long, tx, ct, existingRecord.StrategyId).ConfigureAwait(false);
            }
        }
        // 交易次数仅在订单从未成交变为首次成交（existingRecord.ExecutedQty == 0）时增加一次；
        // 下单时已计数或对账增量补充过的订单不再重复计数
        if (deltaExecutedQty > 0 || deltaCommission > 0)
            await _dailyStats.UpdateDailyStatsAsync(realizedPnl, latestOrder.CommissionAsset == "USDT" ? deltaCommission : 0m,
                existingRecord.ExecutedQty == 0 && deltaExecutedQty > 0, tx, ct).ConfigureAwait(false);

        existingRecord.RequestedQty = latestOrder.RequestedQty > 0 ? latestOrder.RequestedQty : existingRecord.RequestedQty;
        existingRecord.ExecutedQty = latestExecutedQty;
        existingRecord.ExecutedPrice = latestExecutedPrice;
        if (existingRecord.OrderType == OrderType.Market && existingRecord.RequestedPrice is > 0 && latestExecutedQty > 0)
            existingRecord.SlippagePercent = Math.Abs(latestExecutedPrice / existingRecord.RequestedPrice.Value - 1m) * 100m;
        existingRecord.Commission += deltaCommission;
        if (!string.IsNullOrWhiteSpace(latestOrder.CommissionAsset))
            existingRecord.CommissionAsset = latestOrder.CommissionAsset;
        existingRecord.Status = latestStatus;
        existingRecord.CompletedAt = completedAt;
        existingRecord.RealizedPnl += realizedPnl;

        if (long.TryParse(latestOrder.OrderId, out var orderId)) existingRecord.ExchangeOrderId = orderId;
        await UpdateTradeRecordAsync(existingRecord, tx, ct).ConfigureAwait(false);
        tx.Commit();
        return existingRecord;
    }

    private async Task UpdateTradeRecordAsync(TradeRecord record, SqliteTransaction tx, CancellationToken ct)
    {
        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE trade_records
            SET binance_order_id = @orderId, slippage_percent = @slippage, requested_qty = @requestedQty,
                executed_qty = @executedQty,
                executed_price = @executedPrice,
                commission = @commission,
                commission_asset = @commissionAsset,
                status = @status,
                completed_at = @completedAt,
                realized_pnl = @realizedPnl
            WHERE id = @id AND environment = @environment
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@orderId", record.ExchangeOrderId);
        cmd.Parameters.AddWithValue("@slippage", ToDbNullable(record.SlippagePercent));
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@requestedQty", ToDb(record.RequestedQty));
        cmd.Parameters.AddWithValue("@executedQty", ToDb(record.ExecutedQty));
        cmd.Parameters.AddWithValue("@executedPrice", ToDb(record.ExecutedPrice));
        cmd.Parameters.AddWithValue("@commission", ToDb(record.Commission));
        cmd.Parameters.AddWithValue("@commissionAsset", (object?)record.CommissionAsset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (int)record.Status);
        cmd.Parameters.AddWithValue("@completedAt", record.CompletedAt.HasValue ? (object)record.CompletedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@realizedPnl", ToDb(record.RealizedPnl));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>回写单笔交易记录的已实现盈亏（绩效统计用）。</summary>
    public async Task UpdateRealizedPnlAsync(string recordId, decimal pnl, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE trade_records SET realized_pnl = @pnl WHERE id = @id";
        cmd.Parameters.AddWithValue("@pnl", ToDb(pnl));
        cmd.Parameters.AddWithValue("@id", recordId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static TradeRecord ReadTradeRecord(SqliteDataReader reader)
    {
        var record = new TradeRecord
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            StrategyId = reader.GetString(reader.GetOrdinal("strategy_id")),
            Symbol = reader.GetString(reader.GetOrdinal("symbol")),
            Side = (OrderSide)reader.GetInt32(reader.GetOrdinal("side")),
            OrderType = (OrderType)reader.GetInt32(reader.GetOrdinal("order_type")),
            RequestedQty = ReadDecimal(reader, reader.GetOrdinal("requested_qty")),
            ExecutedQty = ReadDecimal(reader, reader.GetOrdinal("executed_qty")),
            ExecutedPrice = ReadDecimal(reader, reader.GetOrdinal("executed_price")),
            Commission = ReadDecimal(reader, reader.GetOrdinal("commission")),
            Status = (TradeRecordStatus)reader.GetInt32(reader.GetOrdinal("status")),
            ExchangeOrderId = reader.GetInt64(reader.GetOrdinal("binance_order_id")),
            ReduceOnly = reader.GetBoolean(reader.GetOrdinal("reduce_only")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture)
        };

        var rpOrd = reader.GetOrdinal("requested_price");
        if (!reader.IsDBNull(rpOrd)) record.RequestedPrice = ReadDecimal(reader, rpOrd);

        var caOrd = reader.GetOrdinal("commission_asset");
        if (!reader.IsDBNull(caOrd)) record.CommissionAsset = reader.GetString(caOrd);

        var arOrd = reader.GetOrdinal("ai_reasoning");
        if (!reader.IsDBNull(arOrd)) record.AIReasoning = reader.GetString(arOrd);

        var coOrd = reader.GetOrdinal("completed_at");
        if (!reader.IsDBNull(coOrd)) record.CompletedAt = DateTime.Parse(reader.GetString(coOrd), CultureInfo.InvariantCulture);

        var spOrd = reader.GetOrdinal("slippage_percent");
        if (!reader.IsDBNull(spOrd)) record.SlippagePercent = ReadDecimal(reader, spOrd);

        var realizedOrd = reader.GetOrdinal("realized_pnl");
        if (!reader.IsDBNull(realizedOrd)) record.RealizedPnl = ReadDecimal(reader, realizedOrd);

        return record;
    }

    private static decimal CalculateEffectiveExecutedPrice(ExchangeOrderResult latestOrder, decimal fallbackPrice)
    {
        if (latestOrder.ExecutedQty > 0 && latestOrder.CumulativeQuoteQty > 0)
            return latestOrder.CumulativeQuoteQty / latestOrder.ExecutedQty;

        if (latestOrder.AveragePrice > 0) return latestOrder.AveragePrice;
        if (latestOrder.Price > 0)
            return latestOrder.Price;

        return fallbackPrice;
    }

    private static TradeRecordStatus MapTradeRecordStatus(string exchangeStatus) => exchangeStatus switch
    {
        "FILLED" => TradeRecordStatus.Filled,
        "PARTIALLY_FILLED" => TradeRecordStatus.PartiallyFilled,
        "CANCELED" or "CANCELLED" => TradeRecordStatus.Cancelled,
        "REJECTED" or "EXPIRED" => TradeRecordStatus.Failed,
        _ => TradeRecordStatus.Pending
    };

    private static bool IsTerminalStatus(TradeRecordStatus status) => status is
        TradeRecordStatus.Filled or TradeRecordStatus.Cancelled or TradeRecordStatus.Failed;
}
