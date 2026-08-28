using System.Globalization;
using System.Text.Json;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易数据持久化服务，管理策略、交易记录和日统计的 SQLite 存储。
/// </summary>
public class TradingDataService : SqliteServiceBase
{
    private const string LiveSpotEnvironment = "crypto-live-spot";
    private const string LiveFuturesEnvironment = "crypto-live-futures";
    private const string FuturesTestnetEnvironment = "crypto-binance-futures-testnet";
    private const string SpotDemoEnvironment = "crypto-binance-spot-demo";

    private readonly TradingEnvironmentService _tradingEnvironmentService;

    public TradingDataService(
        TradingEnvironmentService tradingEnvironmentService,
        ILogger<TradingDataService> logger)
        : base(logger)
    {
        _tradingEnvironmentService = tradingEnvironmentService;
    }

    /// <summary>
    /// 4 种交易模式各自独立的环境 key，确保现货实盘、现货 Demo、合约实盘、合约 Testnet
    /// 的策略、交易记录、持仓、风控配置互不混淆。
    /// </summary>
    private string CurrentEnvironmentKey => _tradingEnvironmentService.CurrentMode switch
    {
        CryptoTradingMode.LiveFutures => LiveFuturesEnvironment,
        CryptoTradingMode.BinanceFuturesTestnet => FuturesTestnetEnvironment,
        CryptoTradingMode.BinanceSpotDemo => SpotDemoEnvironment,
        _ => LiveSpotEnvironment
    };

    /// <summary>
    /// 当前是否为合约模式（合约买卖方向需结合持仓判断开平仓）
    /// </summary>
    private bool IsFuturesMode => _tradingEnvironmentService.CurrentMode is
        CryptoTradingMode.LiveFutures or CryptoTradingMode.BinanceFuturesTestnet;

    #region decimal 存取辅助

    /// <summary>
    /// 金额/数量列以 TEXT（InvariantCulture 十进制字符串）存储，
    /// 避免 REAL（double）存储对高精度小数（如 8 位小数价格）造成精度损失。
    /// </summary>
    private static object ToDb(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="ToDb(decimal)"/>
    private static object ToDbNullable(decimal? value) => value.HasValue ? ToDb(value.Value) : DBNull.Value;

    /// <summary>
    /// 读取金额/数量列，兼容 TEXT（新格式）与 REAL/INTEGER（历史数据）两种存储形态。
    /// </summary>
    private static decimal ReadDecimal(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return 0;

        return reader.GetValue(ordinal) switch
        {
            string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            double d => (decimal)d,
            long l => l,
            _ => 0
        };
    }

    #endregion

    #region 策略 CRUD

    public async Task SaveStrategyAsync(TradingStrategy strategy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO strategies
                (id, environment, symbol, type, status, side, trigger_price, stop_loss_price, take_profit_price,
                 quantity, max_position_percent, custom_params, created_at, last_triggered_at,
                 execution_count, max_executions, trailing_peak_price)
            VALUES
                (@id, @environment, @symbol, @type, @status, @side, @triggerPrice, @slPrice, @tpPrice,
                 @qty, @maxPos, @customParams, @createdAt, @lastTriggered,
                 @execCount, @maxExec, @trailingPeak)
            """;
        cmd.Parameters.AddWithValue("@id", strategy.Id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@symbol", strategy.Symbol);
        cmd.Parameters.AddWithValue("@type", (int)strategy.Type);
        cmd.Parameters.AddWithValue("@status", (int)strategy.Status);
        cmd.Parameters.AddWithValue("@side", (int)strategy.Side);
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies WHERE environment = @environment AND status = @status ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@status", (int)status);
        return await ReadStrategiesAsync(cmd, ct);
    }

    public async Task<List<TradingStrategy>> GetAllStrategiesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies WHERE environment = @environment ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        return await ReadStrategiesAsync(cmd, ct);
    }

    public async Task UpdateStrategyStatusAsync(string id, StrategyStatus status, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM strategies WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateStrategyTriggeredAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
    /// 仅更新策略的最后评估时间（不增加执行计数），用于 AI 信号策略的评估节流：
    /// 无论 Agent 是否实际成交，一次评估后都记入冷却期，避免未成交时每个价格 tick 重复调用 LLM。
    /// </summary>
    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual async Task UpdateStrategyLastTriggeredAtAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
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

    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual async Task UpdateStrategyCustomParamsAsync(string id, string? customParams, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE strategies SET custom_params = @customParams WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@customParams", (object?)customParams ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新策略护栏位（止损/止盈价）。AI 决策生成护栏后调用，
    /// 使 StrategyEngine 的硬性边界评估与 TradeExecutor 的风控检查立即生效。
    /// </summary>
    public async Task UpdateStrategyGuardrailsAsync(
        string id, decimal? stopLossPrice, decimal? takeProfitPrice, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual async Task UpdateStrategyTrailingPeakAsync(string id, decimal? trailingPeakPrice, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE strategies SET trailing_peak_price = @peak WHERE id = @id AND environment = @environment";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@peak", ToDbNullable(trailingPeakPrice));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    #endregion

    #region 交易记录

    public virtual async Task SaveTradeRecordAsync(TradeRecord record, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trade_records
                (id, environment, strategy_id, symbol, side, order_type, requested_qty, executed_qty,
                 requested_price, executed_price, commission, commission_asset, status,
                 binance_order_id, ai_reasoning, created_at, completed_at)
            VALUES
                (@id, @environment, @stratId, @symbol, @side, @orderType, @reqQty, @execQty,
                 @reqPrice, @execPrice, @commission, @commAsset, @status,
                 @binanceId, @aiReasoning, @createdAt, @completedAt)
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
        cmd.Parameters.AddWithValue("@aiReasoning", (object?)record.AIReasoning ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@completedAt", record.CompletedAt.HasValue ? (object)record.CompletedAt.Value.ToString("O") : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<TradeRecord>> GetTradeRecordsAsync(
        string? symbol = null, DateTime? from = null, DateTime? to = null, int limit = 50,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var conditions = new List<string>();
        conditions.Add("environment = @environment");
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var conditions = new List<string>
        {
            "environment = @environment",
            "binance_order_id > 0",
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
        var latestStatus = MapTradeRecordStatus(latestOrder.Status);
        var latestExecutedQty = Math.Max(existingRecord.ExecutedQty, latestOrder.ExecutedQty);
        var latestExecutedPrice = CalculateEffectiveExecutedPrice(latestOrder, existingRecord.ExecutedPrice);
        var deltaExecutedQty = latestExecutedQty - existingRecord.ExecutedQty;
        var deltaCommission = latestOrder.FillCommission > existingRecord.Commission
            ? latestOrder.FillCommission - existingRecord.Commission
            : 0;
        DateTime? completedAt = IsTerminalStatus(latestStatus)
            ? existingRecord.CompletedAt ?? DateTime.UtcNow
            : null;

        var hasMeaningfulChange = deltaExecutedQty > 0
            || deltaCommission > 0
            || latestStatus != existingRecord.Status
            || latestExecutedPrice != existingRecord.ExecutedPrice
            || completedAt != existingRecord.CompletedAt;

        if (!hasMeaningfulChange)
            return existingRecord;

        decimal realizedPnl = 0;
        if (deltaExecutedQty > 0)
        {
            // 合约模式下买卖方向无法单独确定开平仓，需结合本地持仓判断：
            // 买入 = 平空（若存在空头持仓）否则开多；卖出 = 平多（若存在多头持仓）否则开空。
            if (IsFuturesMode)
            {
                var positions = await GetOpenPositionsAsync(existingRecord.Symbol, ct).ConfigureAwait(false);
                if (existingRecord.Side == OrderSide.Buy)
                {
                    if (positions.Any(p => p.Side == PositionSide.Short))
                    {
                        realizedPnl = await ClosePositionFifoAsync(
                            existingRecord.Symbol, deltaExecutedQty, latestExecutedPrice, ct, PositionSide.Short)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await OpenPositionAsync(new Position
                        {
                            Symbol = existingRecord.Symbol,
                            Side = PositionSide.Long,
                            Quantity = deltaExecutedQty,
                            EntryPrice = latestExecutedPrice,
                            StrategyId = existingRecord.StrategyId,
                            OpenedAt = DateTime.UtcNow
                        }, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    if (positions.Any(p => p.Side == PositionSide.Long))
                    {
                        realizedPnl = await ClosePositionFifoAsync(
                            existingRecord.Symbol, deltaExecutedQty, latestExecutedPrice, ct, PositionSide.Long)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await OpenPositionAsync(new Position
                        {
                            Symbol = existingRecord.Symbol,
                            Side = PositionSide.Short,
                            Quantity = deltaExecutedQty,
                            EntryPrice = latestExecutedPrice,
                            StrategyId = existingRecord.StrategyId,
                            OpenedAt = DateTime.UtcNow
                        }, ct).ConfigureAwait(false);
                    }
                }
            }
            else if (existingRecord.Side == OrderSide.Buy)
            {
                await OpenPositionAsync(new Position
                {
                    Symbol = existingRecord.Symbol,
                    Side = PositionSide.Long,
                    Quantity = deltaExecutedQty,
                    EntryPrice = latestExecutedPrice,
                    StrategyId = existingRecord.StrategyId,
                    OpenedAt = DateTime.UtcNow
                }, ct).ConfigureAwait(false);
            }
            else
            {
                realizedPnl = await ClosePositionFifoAsync(
                    existingRecord.Symbol,
                    deltaExecutedQty,
                    latestExecutedPrice,
                    ct).ConfigureAwait(false);
            }
        }

        // 交易次数仅在订单从未成交变为首次成交（existingRecord.ExecutedQty == 0）时增加一次；
        // 下单时已计数或对账增量补充过的订单不再重复计数
        if (deltaExecutedQty > 0 || deltaCommission > 0)
            await UpdateDailyStatsAsync(realizedPnl, deltaCommission,
                countTrade: existingRecord.ExecutedQty == 0 && deltaExecutedQty > 0, ct).ConfigureAwait(false);

        existingRecord.RequestedQty = latestOrder.RequestedQty > 0 ? latestOrder.RequestedQty : existingRecord.RequestedQty;
        existingRecord.ExecutedQty = latestExecutedQty;
        existingRecord.ExecutedPrice = latestExecutedPrice;
        existingRecord.Commission += deltaCommission;
        if (!string.IsNullOrWhiteSpace(latestOrder.CommissionAsset))
            existingRecord.CommissionAsset = latestOrder.CommissionAsset!;
        existingRecord.Status = latestStatus;
        existingRecord.CompletedAt = completedAt;

        await UpdateTradeRecordAsync(existingRecord, ct).ConfigureAwait(false);
        return existingRecord;
    }

    #endregion

    #region 日统计

    /// <summary>
    /// 获取今日日期字符串，用于日统计与账户快照的日期分组键。
    /// 刻意使用本地时间（DateTime.Now）而非 UTC：交易日的切分以用户所在时区为准，
    /// 原实现按 UTC 切分时，亚洲用户在 UTC 16:00 后实际已是次日，导致日统计错位。
    /// 注意：本文件中事件时间戳（如 last_triggered_at、snapshot_at）统一使用 DateTime.UtcNow，
    /// 与此处的日期分组键用途不同——前者记录精确发生时刻（绝对时间），后者划分交易日归属。
    /// </summary>
    private static string GetTodayDateString() => DateTime.Now.ToString("yyyy-MM-dd");

    public virtual async Task<DailyStats> GetTodayStatsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
    public virtual async Task UpdateDailyStatsAsync(decimal pnl, decimal commission, bool countTrade = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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

    /// <summary>
    /// 计算指定交易标的的加权平均买入价（用于多头 PnL 估算）
    /// </summary>
    public async Task<decimal> GetAverageEntryPriceAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CASE WHEN SUM(executed_qty) > 0
                THEN SUM(executed_qty * executed_price) / SUM(executed_qty)
                ELSE 0 END
            FROM trade_records
            WHERE environment = @environment AND symbol = @symbol AND side = @side AND executed_qty > 0 AND status = @status
            """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@symbol", symbol);
        cmd.Parameters.AddWithValue("@side", (int)OrderSide.Buy);
        cmd.Parameters.AddWithValue("@status", (int)TradeRecordStatus.Filled);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is double d)
            return (decimal)d;
        return 0;
    }

    /// <summary>
    /// 计算指定交易标的的加权平均卖出价（用于空头平仓 PnL 估算）
    /// </summary>
    public async Task<decimal> GetAverageSellPriceAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CASE WHEN SUM(executed_qty) > 0
                THEN SUM(executed_qty * executed_price) / SUM(executed_qty)
                ELSE 0 END
            FROM trade_records
            WHERE environment = @environment AND symbol = @symbol AND side = @side AND executed_qty > 0 AND status = @status
            """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@symbol", symbol);
        cmd.Parameters.AddWithValue("@side", (int)OrderSide.Sell);
        cmd.Parameters.AddWithValue("@status", (int)TradeRecordStatus.Filled);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is double d)
            return (decimal)d;
        return 0;
    }

    #endregion

    #region 持仓 FIFO 追踪

    /// <summary>
    /// 开仓：插入一条新的持仓记录
    /// </summary>
    public async Task OpenPositionAsync(Position position, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
    public virtual async Task<decimal> ClosePositionFifoAsync(
        string symbol, decimal closeQty, decimal closePrice,
        CancellationToken ct = default, PositionSide side = PositionSide.Long)
    {
        if (closeQty <= 0)
            return 0;

        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
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
                updateCmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
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
    public virtual async Task<List<Position>> GetOpenPositionsAsync(string? symbol = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual async Task<decimal> GetOpenPositionAvgEntryPriceAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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

    #endregion

    #region 风控配置持久化

    /// <summary>
    /// 保存每日账户快照（用于计算最大回撤）
    /// </summary>
    public async Task SaveAccountSnapshotAsync(decimal totalValueUsdt, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
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
    /// 获取历史最高账户价值（用于计算回撤）
    /// </summary>
    public async Task<decimal> GetPeakAccountValueAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // 金额列以 TEXT 存储，MAX 会按字符串比较，需 CAST 为数值后再取最大值
        cmd.CommandText = "SELECT MAX(CAST(total_value_usdt AS REAL)) FROM account_snapshots WHERE environment = @environment";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is double d)
            return (decimal)d;
        return 0;
    }

    public virtual async Task<RiskConfig> LoadRiskConfigAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT config_json FROM risk_config WHERE environment = @environment AND market_type = @marketType";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@marketType", (int)MarketType.Crypto);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is not string json || string.IsNullOrEmpty(json))
            return new RiskConfig();
        try
        {
            return JsonSerializer.Deserialize<RiskConfig>(json) ?? new RiskConfig();
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "风控配置反序列化失败，将使用默认配置");
            return new RiskConfig();
        }
    }

    public async Task SaveRiskConfigAsync(RiskConfig config, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO risk_config (environment, market_type, config_json, updated_at)
            VALUES (@environment, @marketType, @configJson, @updatedAt)
            ON CONFLICT(environment, market_type) DO UPDATE SET config_json = @configJson, updated_at = @updatedAt
            """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@marketType", (int)MarketType.Crypto);
        cmd.Parameters.AddWithValue("@configJson", JsonSerializer.Serialize(config));
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    #endregion

    #region 内部方法

    protected override async Task InitializeDatabaseAsync()
    {
        try
        {
            await using var conn = await OpenConnectionAsync();
            await MigrateDatabaseSchemaAsync(conn).ConfigureAwait(false);
            Logger.LogInformation("交易数据库初始化完成");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "初始化交易数据库失败");
            throw new InvalidOperationException("交易数据库初始化失败，应用无法继续运行", ex);
        }
    }

    /// <summary>
    /// 在同一事务内完成建表、旧结构迁移和索引创建，保证升级失败时不会留下半迁移状态。
    /// </summary>
    internal static async Task MigrateDatabaseSchemaAsync(SqliteConnection conn)
    {
        await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            await CreateTablesAsync(conn, transaction).ConfigureAwait(false);
            await EnsureEnvironmentSchemaAsync(conn, transaction).ConfigureAwait(false);
            await MigrateMoneyColumnsToTextAsync(conn, transaction).ConfigureAwait(false);
            await CreateIndexesAsync(conn, transaction).ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 金额/数量列迁移计划：这些列在新 schema 中以 TEXT（十进制字符串）存储。
    /// </summary>
    private static readonly (string Table, string[] MoneyColumns)[] MoneyColumnMigrations =
    [
        ("strategies", ["trigger_price", "stop_loss_price", "take_profit_price", "quantity", "max_position_percent", "trailing_peak_price"]),
        ("trade_records", ["requested_qty", "executed_qty", "requested_price", "executed_price", "commission"]),
        ("daily_stats", ["total_pnl", "total_commission"]),
        ("positions", ["quantity", "entry_price", "closed_quantity"]),
        ("account_snapshots", ["total_value_usdt"])
    ];

    /// <summary>各表建表 DDL（金额/数量列为 TEXT），供新建库与迁移重建共用。</summary>
    private static readonly IReadOnlyDictionary<string, string> TableDefinitions = new Dictionary<string, string>
    {
        ["strategies"] = """
            CREATE TABLE IF NOT EXISTS strategies (
                id TEXT PRIMARY KEY,
                environment TEXT NOT NULL DEFAULT 'crypto-live-spot',
                symbol TEXT NOT NULL,
                type INTEGER NOT NULL,
                status INTEGER NOT NULL,
                side INTEGER NOT NULL,
                trigger_price TEXT NOT NULL,
                stop_loss_price TEXT,
                take_profit_price TEXT,
                quantity TEXT NOT NULL,
                max_position_percent TEXT,
                custom_params TEXT,
                created_at TEXT NOT NULL,
                last_triggered_at TEXT,
                execution_count INTEGER DEFAULT 0,
                max_executions INTEGER,
                trailing_peak_price TEXT
            )
            """,
        ["trade_records"] = """
            CREATE TABLE IF NOT EXISTS trade_records (
                id TEXT PRIMARY KEY,
                environment TEXT NOT NULL DEFAULT 'crypto-live-spot',
                strategy_id TEXT NOT NULL,
                symbol TEXT NOT NULL,
                side INTEGER NOT NULL,
                order_type INTEGER NOT NULL,
                requested_qty TEXT NOT NULL,
                executed_qty TEXT NOT NULL,
                requested_price TEXT,
                executed_price TEXT NOT NULL,
                commission TEXT DEFAULT '0',
                commission_asset TEXT,
                status INTEGER NOT NULL,
                binance_order_id INTEGER,
                ai_reasoning TEXT,
                created_at TEXT NOT NULL,
                completed_at TEXT,
                FOREIGN KEY (strategy_id) REFERENCES strategies(id)
            )
            """,

        ["daily_stats"] = """
            CREATE TABLE IF NOT EXISTS daily_stats (
                environment TEXT NOT NULL,
                date TEXT NOT NULL,
                trade_count INTEGER DEFAULT 0,
                total_pnl TEXT DEFAULT '0',
                total_commission TEXT DEFAULT '0',
                PRIMARY KEY (environment, date)
            )
            """,
        ["positions"] = """
            CREATE TABLE IF NOT EXISTS positions (
                id TEXT PRIMARY KEY,
                environment TEXT NOT NULL DEFAULT 'crypto-live-spot',
                symbol TEXT NOT NULL,
                side INTEGER NOT NULL,
                quantity TEXT NOT NULL,
                entry_price TEXT NOT NULL,
                closed_quantity TEXT DEFAULT '0',
                strategy_id TEXT,
                opened_at TEXT NOT NULL
            )
            """,
        ["account_snapshots"] = """
            CREATE TABLE IF NOT EXISTS account_snapshots (
                environment TEXT NOT NULL,
                date TEXT NOT NULL,
                total_value_usdt TEXT NOT NULL,
                snapshot_at TEXT NOT NULL,
                PRIMARY KEY (environment, date)
            )
            """,
        ["risk_config"] = """
            CREATE TABLE IF NOT EXISTS risk_config (
                environment TEXT NOT NULL,
                market_type INTEGER NOT NULL,
                config_json TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (environment, market_type)
            )
            """
    };

    private static async Task CreateTablesAsync(SqliteConnection conn, SqliteTransaction transaction)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = string.Join(";\n\n", TableDefinitions.Values) + ";";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 将历史库中金额列由 REAL 迁移为 TEXT（十进制字符串），消除 double 存储精度损失。
    /// SQLite 不支持直接修改列类型，通过"重命名旧表 → 按新 DDL 建表 → CAST 搬运 → 删旧表"重建。
    /// 重命名期间启用 PRAGMA legacy_alter_table = ON，避免其他表的 FOREIGN KEY 引用被改写到旧表名；
    /// 旧表索引统一先删除，迁移完成后由 CreateIndexesAsync 重建。
    /// </summary>
    internal static async Task MigrateMoneyColumnsToTextAsync(SqliteConnection conn, SqliteTransaction transaction)
    {
        // 先探测哪些表需要迁移，避免无谓的索引删除与表重建
        var pendingTables = new List<(string Table, HashSet<string> MoneyColumns, List<(string Name, string Type)> Columns)>();
        foreach (var (table, moneyColumns) in MoneyColumnMigrations)
        {
            var columns = await GetTableColumnsAsync(conn, transaction, table).ConfigureAwait(false);
            if (columns.Count == 0)
                continue;

            var moneySet = moneyColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var needsMigration = columns.Any(c => moneySet.Contains(c.Name)
                && !c.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase));
            if (needsMigration)
                pendingTables.Add((table, moneySet, columns));
        }

        if (pendingTables.Count == 0)
            return;

        await ExecuteSchemaCommandAsync(conn, transaction, "PRAGMA legacy_alter_table = ON").ConfigureAwait(false);

        // 旧表重命名后其索引仍占用原名称，必须先删除，否则后续 CREATE INDEX IF NOT EXISTS 会静默跳过
        await ExecuteSchemaCommandAsync(conn, transaction, """
            DROP INDEX IF EXISTS idx_strategies_symbol;
            DROP INDEX IF EXISTS idx_strategies_status;
            DROP INDEX IF EXISTS idx_strategies_environment_status;
            DROP INDEX IF EXISTS idx_records_strategy;
            DROP INDEX IF EXISTS idx_records_symbol;
            DROP INDEX IF EXISTS idx_records_created;
            DROP INDEX IF EXISTS idx_records_environment_created;
            DROP INDEX IF EXISTS idx_positions_symbol;
            DROP INDEX IF EXISTS idx_positions_side;
            DROP INDEX IF EXISTS idx_positions_environment_symbol;
            """).ConfigureAwait(false);

        foreach (var (table, moneyColumns, columns) in pendingTables)
        {
            var legacyTable = $"{table}_money_legacy";

            await ExecuteSchemaCommandAsync(conn, transaction, $"ALTER TABLE {table} RENAME TO {legacyTable}").ConfigureAwait(false);
            await ExecuteSchemaCommandAsync(conn, transaction, TableDefinitions[table] + ";").ConfigureAwait(false);

            // CAST(col AS TEXT) 将历史 REAL 值转为十进制字符串文本
            var columnList = string.Join(", ", columns.Select(c => c.Name));
            var selectList = string.Join(", ", columns.Select(c => moneyColumns.Contains(c.Name) ? $"CAST({c.Name} AS TEXT)" : c.Name));
            await ExecuteSchemaCommandAsync(conn, transaction, $"INSERT INTO {table} ({columnList}) SELECT {selectList} FROM {legacyTable}").ConfigureAwait(false);
            await ExecuteSchemaCommandAsync(conn, transaction, $"DROP TABLE {legacyTable}").ConfigureAwait(false);
        }

        await ExecuteSchemaCommandAsync(conn, transaction, "PRAGMA legacy_alter_table = OFF").ConfigureAwait(false);
    }

    private static async Task ExecuteSchemaCommandAsync(SqliteConnection conn, SqliteTransaction transaction, string commandText)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<List<(string Name, string Type)>> GetTableColumnsAsync(
        SqliteConnection conn, SqliteTransaction transaction, string tableName)
    {
        var columns = new List<(string Name, string Type)>();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            columns.Add((reader.GetString(1), reader.GetString(2)));
        }
        return columns;
    }

    private static async Task CreateIndexesAsync(SqliteConnection conn, SqliteTransaction transaction)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_strategies_symbol ON strategies(symbol);
            CREATE INDEX IF NOT EXISTS idx_strategies_status ON strategies(status);
            CREATE INDEX IF NOT EXISTS idx_strategies_environment_status ON strategies(environment, status, created_at);
            CREATE INDEX IF NOT EXISTS idx_records_strategy ON trade_records(strategy_id);
            CREATE INDEX IF NOT EXISTS idx_records_symbol ON trade_records(symbol);
            CREATE INDEX IF NOT EXISTS idx_records_created ON trade_records(created_at);
            CREATE INDEX IF NOT EXISTS idx_records_environment_created ON trade_records(environment, created_at);
            CREATE INDEX IF NOT EXISTS idx_positions_symbol ON positions(symbol);
            CREATE INDEX IF NOT EXISTS idx_positions_side ON positions(symbol, side);
            CREATE INDEX IF NOT EXISTS idx_positions_environment_symbol ON positions(environment, symbol, side);
            """;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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
            TriggerPrice = (decimal)reader.GetDouble(reader.GetOrdinal("trigger_price")),
            Quantity = (decimal)reader.GetDouble(reader.GetOrdinal("quantity")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture),
            ExecutionCount = reader.GetInt32(reader.GetOrdinal("execution_count"))
        };

        var slOrd = reader.GetOrdinal("stop_loss_price");
        if (!reader.IsDBNull(slOrd)) strategy.StopLossPrice = (decimal)reader.GetDouble(slOrd);

        var tpOrd = reader.GetOrdinal("take_profit_price");
        if (!reader.IsDBNull(tpOrd)) strategy.TakeProfitPrice = (decimal)reader.GetDouble(tpOrd);

        var mpOrd = reader.GetOrdinal("max_position_percent");
        if (!reader.IsDBNull(mpOrd)) strategy.MaxPositionPercent = (decimal)reader.GetDouble(mpOrd);

        var cpOrd = reader.GetOrdinal("custom_params");
        if (!reader.IsDBNull(cpOrd)) strategy.CustomParams = reader.GetString(cpOrd);

        var ltOrd = reader.GetOrdinal("last_triggered_at");
        if (!reader.IsDBNull(ltOrd)) strategy.LastTriggeredAt = DateTime.Parse(reader.GetString(ltOrd), CultureInfo.InvariantCulture);

        var meOrd = reader.GetOrdinal("max_executions");
        if (!reader.IsDBNull(meOrd)) strategy.MaxExecutions = reader.GetInt32(meOrd);

        var trailingOrd = reader.GetOrdinal("trailing_peak_price");
        if (!reader.IsDBNull(trailingOrd)) strategy.TrailingPeakPrice = (decimal)reader.GetDouble(trailingOrd);

        return strategy;
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
            RequestedQty = (decimal)reader.GetDouble(reader.GetOrdinal("requested_qty")),
            ExecutedQty = (decimal)reader.GetDouble(reader.GetOrdinal("executed_qty")),
            ExecutedPrice = (decimal)reader.GetDouble(reader.GetOrdinal("executed_price")),
            Commission = (decimal)reader.GetDouble(reader.GetOrdinal("commission")),
            Status = (TradeRecordStatus)reader.GetInt32(reader.GetOrdinal("status")),
            ExchangeOrderId = reader.GetInt64(reader.GetOrdinal("binance_order_id")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture)
        };

        var rpOrd = reader.GetOrdinal("requested_price");
        if (!reader.IsDBNull(rpOrd)) record.RequestedPrice = (decimal)reader.GetDouble(rpOrd);

        var caOrd = reader.GetOrdinal("commission_asset");
        if (!reader.IsDBNull(caOrd)) record.CommissionAsset = reader.GetString(caOrd);

        var arOrd = reader.GetOrdinal("ai_reasoning");
        if (!reader.IsDBNull(arOrd)) record.AIReasoning = reader.GetString(arOrd);

        var coOrd = reader.GetOrdinal("completed_at");
        if (!reader.IsDBNull(coOrd)) record.CompletedAt = DateTime.Parse(reader.GetString(coOrd), CultureInfo.InvariantCulture);

        return record;
    }

    private async Task UpdateTradeRecordAsync(TradeRecord record, CancellationToken ct)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE trade_records
            SET requested_qty = @requestedQty,
                executed_qty = @executedQty,
                executed_price = @executedPrice,
                commission = @commission,
                commission_asset = @commissionAsset,
                status = @status,
                completed_at = @completedAt
            WHERE id = @id AND environment = @environment
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@requestedQty", ToDb(record.RequestedQty));
        cmd.Parameters.AddWithValue("@executedQty", ToDb(record.ExecutedQty));
        cmd.Parameters.AddWithValue("@executedPrice", ToDb(record.ExecutedPrice));
        cmd.Parameters.AddWithValue("@commission", ToDb(record.Commission));
        cmd.Parameters.AddWithValue("@commissionAsset", (object?)record.CommissionAsset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (int)record.Status);
        cmd.Parameters.AddWithValue("@completedAt", record.CompletedAt.HasValue ? (object)record.CompletedAt.Value.ToString("O") : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static decimal CalculateEffectiveExecutedPrice(ExchangeOrderResult latestOrder, decimal fallbackPrice)
    {
        if (latestOrder.ExecutedQty > 0 && latestOrder.CumulativeQuoteQty > 0)
            return latestOrder.CumulativeQuoteQty / latestOrder.ExecutedQty;

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

    private static async Task EnsureEnvironmentSchemaAsync(
        SqliteConnection conn,
        SqliteTransaction transaction)
    {
        await EnsureColumnAsync(conn, transaction, "strategies", "environment", $"TEXT NOT NULL DEFAULT '{LiveSpotEnvironment}'").ConfigureAwait(false);
        await EnsureColumnAsync(conn, transaction, "trade_records", "environment", $"TEXT NOT NULL DEFAULT '{LiveSpotEnvironment}'").ConfigureAwait(false);
        await EnsureColumnAsync(conn, transaction, "positions", "environment", $"TEXT NOT NULL DEFAULT '{LiveSpotEnvironment}'").ConfigureAwait(false);
        await MigrateDailyStatsAsync(conn, transaction).ConfigureAwait(false);
        await MigrateAccountSnapshotsAsync(conn, transaction).ConfigureAwait(false);
        await MigrateRiskConfigAsync(conn, transaction).ConfigureAwait(false);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection conn,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        if (await ColumnExistsAsync(conn, transaction, tableName, columnName).ConfigureAwait(false))
            return;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task MigrateDailyStatsAsync(
        SqliteConnection conn,
        SqliteTransaction transaction)
    {
        if (await ColumnExistsAsync(conn, transaction, "daily_stats", "environment").ConfigureAwait(false))
            return;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""
            ALTER TABLE daily_stats RENAME TO daily_stats_legacy;

            CREATE TABLE daily_stats (
                environment TEXT NOT NULL,
                date TEXT NOT NULL,
                trade_count INTEGER DEFAULT 0,
                total_pnl REAL DEFAULT 0,
                total_commission REAL DEFAULT 0,
                PRIMARY KEY (environment, date)
            );

            INSERT INTO daily_stats (environment, date, trade_count, total_pnl, total_commission)
            SELECT '{LiveSpotEnvironment}', date, trade_count, total_pnl, total_commission
            FROM daily_stats_legacy;

            DROP TABLE daily_stats_legacy;
            """;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task MigrateAccountSnapshotsAsync(
        SqliteConnection conn,
        SqliteTransaction transaction)
    {
        if (await ColumnExistsAsync(conn, transaction, "account_snapshots", "environment").ConfigureAwait(false))
            return;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""
            ALTER TABLE account_snapshots RENAME TO account_snapshots_legacy;

            CREATE TABLE account_snapshots (
                environment TEXT NOT NULL,
                date TEXT NOT NULL,
                total_value_usdt REAL NOT NULL,
                snapshot_at TEXT NOT NULL,
                PRIMARY KEY (environment, date)
            );

            INSERT INTO account_snapshots (environment, date, total_value_usdt, snapshot_at)
            SELECT '{LiveSpotEnvironment}', date, total_value_usdt, snapshot_at
            FROM account_snapshots_legacy;

            DROP TABLE account_snapshots_legacy;
            """;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task MigrateRiskConfigAsync(
        SqliteConnection conn,
        SqliteTransaction transaction)
    {
        if (await ColumnExistsAsync(conn, transaction, "risk_config", "environment").ConfigureAwait(false))
            return;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""
            ALTER TABLE risk_config RENAME TO risk_config_legacy;

            CREATE TABLE risk_config (
                environment TEXT NOT NULL,
                market_type INTEGER NOT NULL,
                config_json TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (environment, market_type)
            );

            INSERT INTO risk_config (environment, market_type, config_json, updated_at)
            SELECT '{LiveSpotEnvironment}', market_type, config_json, updated_at
            FROM risk_config_legacy;

            DROP TABLE risk_config_legacy;
            """;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection conn,
        SqliteTransaction transaction,
        string tableName,
        string columnName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task<List<TradingStrategy>> ReadStrategiesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var strategies = new List<TradingStrategy>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            strategies.Add(ReadStrategy(reader));
        return strategies;
    }

    #endregion
}
