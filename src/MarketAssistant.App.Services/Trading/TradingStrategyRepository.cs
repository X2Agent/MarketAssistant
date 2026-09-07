using System.Globalization;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 策略 CRUD 仓储（internal）：策略表的持久化与查询。
/// </summary>
internal sealed class TradingStrategyRepository : TradingRepositoryBase
{
    public TradingStrategyRepository(
        TradingSchemaInitializer schema,
        TradingEnvironmentService environment,
        Microsoft.Extensions.Logging.ILogger logger)
        : base(schema, environment, logger)
    {
    }

    public async Task SaveStrategyAsync(TradingStrategy strategy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO strategies
                (id, environment, symbol, type, status, side, order_type, slippage_tolerance,
                 trigger_price, stop_loss_price, take_profit_price,
                 quantity, max_position_percent, custom_params, created_at, last_triggered_at,
                 execution_count, max_executions, trailing_peak_price)
            VALUES
                (@id, @environment, @symbol, @type, @status, @side, @orderType, @slippage,
                 @triggerPrice, @slPrice, @tpPrice,
                 @qty, @maxPos, @customParams, @createdAt, @lastTriggered,
                 @execCount, @maxExec, @trailingPeak)
            """;
        cmd.Parameters.AddWithValue("@id", strategy.Id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@symbol", strategy.Symbol);
        cmd.Parameters.AddWithValue("@type", (int)strategy.Type);
        cmd.Parameters.AddWithValue("@status", (int)strategy.Status);
        cmd.Parameters.AddWithValue("@side", (int)strategy.Side);
        cmd.Parameters.AddWithValue("@orderType", (int)strategy.OrderType);
        cmd.Parameters.AddWithValue("@slippage", ToDb(strategy.SlippageTolerance));
        cmd.Parameters.AddWithValue("@triggerPrice", ToDb(strategy.TriggerPrice));
        cmd.Parameters.AddWithValue("@slPrice", ToDbNullable(strategy.StopLossPrice));
        cmd.Parameters.AddWithValue("@tpPrice", ToDbNullable(strategy.TakeProfitPrice));
        cmd.Parameters.AddWithValue("@qty", ToDb(strategy.Quantity));
        cmd.Parameters.AddWithValue("@maxPos", ToDbNullable(strategy.MaxPositionPercent));
        cmd.Parameters.AddWithValue("@customParams", (object?)strategy.CustomParams ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", strategy.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastTriggered", strategy.LastTriggeredAt.HasValue ? (object)strategy.LastTriggeredAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@execCount", strategy.ExecutionCount);
        cmd.Parameters.AddWithValue("@maxExec", strategy.MaxExecutions.HasValue ? (object)strategy.MaxExecutions.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@trailingPeak", ToDbNullable(strategy.TrailingPeakPrice));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<TradingStrategy?> GetStrategyAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadStrategy(reader) : null;
    }

    public async Task<List<TradingStrategy>> GetStrategiesByStatusAsync(StrategyStatus status, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies WHERE environment = @environment AND status = @status ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@status", (int)status);
        return await ReadStrategiesAsync(cmd, ct);
    }

    public async Task<List<TradingStrategy>> GetAllStrategiesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies WHERE environment = @environment ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        return await ReadStrategiesAsync(cmd, ct);
    }

    public async Task UpdateStrategyStatusAsync(string id, StrategyStatus status, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE strategies SET status = @status WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@status", (int)status);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteStrategyAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM strategies WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateStrategyTriggeredAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE strategies
            SET last_triggered_at = @time, execution_count = execution_count + 1
            WHERE id = @id AND environment = @environment
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 仅更新策略的最后评估时间（不增加执行计数），用于 AI 信号策略的评估节流。
    /// </summary>
    public async Task UpdateStrategyLastTriggeredAtAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE strategies
            SET last_triggered_at = @time
            WHERE id = @id AND environment = @environment
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 原子地更新策略触发计数和自定义参数（用于网格交易，防止计数已更新但交易未执行的状态不一致）
    /// </summary>
    public async Task UpdateStrategyTriggeredWithParamsAsync(string id, string? customParams, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                UPDATE strategies
                SET last_triggered_at = @time,
                    execution_count = execution_count + 1,
                    custom_params = @customParams
                WHERE id = @id AND environment = @environment
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
            cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@customParams", (object?)customParams ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task UpdateStrategyCustomParamsAsync(string id, string? customParams, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE strategies SET custom_params = @customParams WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@customParams", (object?)customParams ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新策略护栏位（止损/止盈价）。
    /// </summary>
    public async Task UpdateStrategyGuardrailsAsync(
        string id, decimal? stopLossPrice, decimal? takeProfitPrice, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE strategies SET stop_loss_price = @stopLoss, take_profit_price = @takeProfit
            WHERE id = @id AND environment = @environment
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@stopLoss", ToDbNullable(stopLossPrice));
        cmd.Parameters.AddWithValue("@takeProfit", ToDbNullable(takeProfitPrice));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新追踪止损的峰值/谷值价格（持久化，防止重启丢失）
    /// </summary>
    public async Task UpdateStrategyTrailingPeakAsync(string id, decimal? trailingPeakPrice, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE strategies SET trailing_peak_price = @peak WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@peak", ToDbNullable(trailingPeakPrice));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static TradingStrategy ReadStrategy(SqliteDataReader reader)
    {
        var strategy = new TradingStrategy
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Symbol = reader.GetString(reader.GetOrdinal("symbol")),
            Type = (StrategyType)reader.GetInt32(reader.GetOrdinal("type")),
            Status = (StrategyStatus)reader.GetInt32(reader.GetOrdinal("status")),
            Side = (OrderSide)reader.GetInt32(reader.GetOrdinal("side")),
            OrderType = (OrderType)reader.GetInt32(reader.GetOrdinal("order_type")),
            TriggerPrice = ReadDecimal(reader, reader.GetOrdinal("trigger_price")),
            Quantity = ReadDecimal(reader, reader.GetOrdinal("quantity")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture),
            ExecutionCount = reader.GetInt32(reader.GetOrdinal("execution_count"))
        };

        var slOrd = reader.GetOrdinal("stop_loss_price");
        if (!reader.IsDBNull(slOrd)) strategy.StopLossPrice = ReadDecimal(reader, slOrd);

        var tpOrd = reader.GetOrdinal("take_profit_price");
        if (!reader.IsDBNull(tpOrd)) strategy.TakeProfitPrice = ReadDecimal(reader, tpOrd);

        var mpOrd = reader.GetOrdinal("max_position_percent");
        if (!reader.IsDBNull(mpOrd)) strategy.MaxPositionPercent = ReadDecimal(reader, mpOrd);

        var slipOrd = reader.GetOrdinal("slippage_tolerance");
        if (!reader.IsDBNull(slipOrd))
        {
            strategy.SlippageTolerance = ReadDecimal(reader, slipOrd);
        }

        var cpOrd = reader.GetOrdinal("custom_params");
        if (!reader.IsDBNull(cpOrd)) strategy.CustomParams = reader.GetString(cpOrd);

        var ltOrd = reader.GetOrdinal("last_triggered_at");
        if (!reader.IsDBNull(ltOrd)) strategy.LastTriggeredAt = DateTime.Parse(reader.GetString(ltOrd), CultureInfo.InvariantCulture);

        var meOrd = reader.GetOrdinal("max_executions");
        if (!reader.IsDBNull(meOrd)) strategy.MaxExecutions = reader.GetInt32(meOrd);

        var trailingOrd = reader.GetOrdinal("trailing_peak_price");
        if (!reader.IsDBNull(trailingOrd)) strategy.TrailingPeakPrice = ReadDecimal(reader, trailingOrd);

        return strategy;
    }

    private static async Task<List<TradingStrategy>> ReadStrategiesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var strategies = new List<TradingStrategy>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            strategies.Add(ReadStrategy(reader));
        return strategies;
    }
}
