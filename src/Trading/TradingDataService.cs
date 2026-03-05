using System.Globalization;
using System.Text.Json;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 交易数据持久化服务，管理策略、交易记录和日统计的 SQLite 存储
/// </summary>
public class TradingDataService : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<TradingDataService> _logger;
    private readonly Task _initializeTask;

    public TradingDataService(ILogger<TradingDataService> logger)
    {
        _logger = logger;

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName);
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "trading.db");
        _connectionString = $"Data Source={dbPath}";

        _initializeTask = InitializeDatabaseAsync();
    }

    #region 策略 CRUD

    public async Task SaveStrategyAsync(TradingStrategy strategy, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO strategies
                (id, symbol, type, status, side, trigger_price, stop_loss_price, take_profit_price,
                 quantity, max_position_percent, custom_params, created_at, last_triggered_at,
                 execution_count, max_executions)
            VALUES
                (@id, @symbol, @type, @status, @side, @triggerPrice, @slPrice, @tpPrice,
                 @qty, @maxPos, @customParams, @createdAt, @lastTriggered,
                 @execCount, @maxExec)
            """;
        cmd.Parameters.AddWithValue("@id", strategy.Id);
        cmd.Parameters.AddWithValue("@symbol", strategy.Symbol);
        cmd.Parameters.AddWithValue("@type", (int)strategy.Type);
        cmd.Parameters.AddWithValue("@status", (int)strategy.Status);
        cmd.Parameters.AddWithValue("@side", (int)strategy.Side);
        cmd.Parameters.AddWithValue("@triggerPrice", (double)strategy.TriggerPrice);
        cmd.Parameters.AddWithValue("@slPrice", strategy.StopLossPrice.HasValue ? (object)(double)strategy.StopLossPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@tpPrice", strategy.TakeProfitPrice.HasValue ? (object)(double)strategy.TakeProfitPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@qty", (double)strategy.Quantity);
        cmd.Parameters.AddWithValue("@maxPos", strategy.MaxPositionPercent.HasValue ? (object)(double)strategy.MaxPositionPercent.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@customParams", (object?)strategy.CustomParams ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", strategy.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastTriggered", strategy.LastTriggeredAt.HasValue ? (object)strategy.LastTriggeredAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@execCount", strategy.ExecutionCount);
        cmd.Parameters.AddWithValue("@maxExec", strategy.MaxExecutions.HasValue ? (object)strategy.MaxExecutions.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TradingStrategy?> GetStrategyAsync(string id, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadStrategy(reader) : null;
    }

    public async Task<List<TradingStrategy>> GetStrategiesByStatusAsync(StrategyStatus status, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies WHERE status = @status ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@status", (int)status);
        return await ReadStrategiesAsync(cmd, ct);
    }

    public async Task<List<TradingStrategy>> GetAllStrategiesAsync(CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM strategies ORDER BY created_at DESC";
        return await ReadStrategiesAsync(cmd, ct);
    }

    public async Task UpdateStrategyStatusAsync(string id, StrategyStatus status, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE strategies SET status = @status WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", (int)status);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteStrategyAsync(string id, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM strategies WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateStrategyTriggeredAsync(string id, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE strategies
            SET last_triggered_at = @time, execution_count = execution_count + 1
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    #endregion

    #region 交易记录

    public async Task SaveTradeRecordAsync(TradeRecord record, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trade_records
                (id, strategy_id, symbol, side, order_type, requested_qty, executed_qty,
                 requested_price, executed_price, commission, commission_asset, status,
                 binance_order_id, ai_reasoning, created_at, completed_at)
            VALUES
                (@id, @stratId, @symbol, @side, @orderType, @reqQty, @execQty,
                 @reqPrice, @execPrice, @commission, @commAsset, @status,
                 @binanceId, @aiReasoning, @createdAt, @completedAt)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@stratId", record.StrategyId);
        cmd.Parameters.AddWithValue("@symbol", record.Symbol);
        cmd.Parameters.AddWithValue("@side", (int)record.Side);
        cmd.Parameters.AddWithValue("@orderType", (int)record.OrderType);
        cmd.Parameters.AddWithValue("@reqQty", (double)record.RequestedQty);
        cmd.Parameters.AddWithValue("@execQty", (double)record.ExecutedQty);
        cmd.Parameters.AddWithValue("@reqPrice", record.RequestedPrice.HasValue ? (object)(double)record.RequestedPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@execPrice", (double)record.ExecutedPrice);
        cmd.Parameters.AddWithValue("@commission", (double)record.Commission);
        cmd.Parameters.AddWithValue("@commAsset", (object?)record.CommissionAsset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (int)record.Status);
        cmd.Parameters.AddWithValue("@binanceId", record.BinanceOrderId);
        cmd.Parameters.AddWithValue("@aiReasoning", (object?)record.AIReasoning ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@completedAt", record.CompletedAt.HasValue ? (object)record.CompletedAt.Value.ToString("O") : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<TradeRecord>> GetTradeRecordsAsync(
        string? symbol = null, DateTime? from = null, DateTime? to = null, int limit = 50,
        CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var conditions = new List<string>();
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
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM trade_records WHERE strategy_id = @stratId ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@stratId", strategyId);

        var records = new List<TradeRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            records.Add(ReadTradeRecord(reader));
        return records;
    }

    #endregion

    #region 日统计

    public async Task<DailyStats> GetTodayStatsAsync(CancellationToken ct = default)
    {
        await _initializeTask;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM daily_stats WHERE date = @date";
        cmd.Parameters.AddWithValue("@date", today);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return new DailyStats
            {
                Date = reader.GetString(reader.GetOrdinal("date")),
                TradeCount = reader.GetInt32(reader.GetOrdinal("trade_count")),
                TotalPnl = (decimal)reader.GetDouble(reader.GetOrdinal("total_pnl")),
                TotalCommission = (decimal)reader.GetDouble(reader.GetOrdinal("total_commission"))
            };
        }

        return new DailyStats { Date = today };
    }

    public async Task UpdateDailyStatsAsync(decimal pnl, decimal commission, CancellationToken ct = default)
    {
        await _initializeTask;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO daily_stats (date, trade_count, total_pnl, total_commission)
            VALUES (@date, 1, @pnl, @comm)
            ON CONFLICT(date) DO UPDATE SET
                trade_count = trade_count + 1,
                total_pnl = total_pnl + @pnl,
                total_commission = total_commission + @comm
            """;
        cmd.Parameters.AddWithValue("@date", today);
        cmd.Parameters.AddWithValue("@pnl", (double)pnl);
        cmd.Parameters.AddWithValue("@comm", (double)commission);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 计算指定交易对的加权平均买入价（用于 PnL 估算）
    /// </summary>
    public async Task<decimal> GetAverageEntryPriceAsync(string symbol, CancellationToken ct = default)
    {
        await _initializeTask;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CASE WHEN SUM(executed_qty) > 0
                THEN SUM(executed_qty * executed_price) / SUM(executed_qty)
                ELSE 0 END
            FROM trade_records
            WHERE symbol = @symbol AND side = @side AND executed_qty > 0 AND status = @status
            """;
        cmd.Parameters.AddWithValue("@symbol", symbol);
        cmd.Parameters.AddWithValue("@side", (int)OrderSide.Buy);
        cmd.Parameters.AddWithValue("@status", (int)TradeRecordStatus.Filled);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is double d)
            return (decimal)d;
        return 0;
    }

    #endregion

    #region 风控配置持久化

    private const string RiskConfigKey = "TradingRiskConfig";

    public RiskConfig LoadRiskConfig()
    {
        var json = Preferences.Default.Get(RiskConfigKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return new RiskConfig();
        try
        {
            return JsonSerializer.Deserialize<RiskConfig>(json) ?? new RiskConfig();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "风控配置反序列化失败，将使用默认配置");
            return new RiskConfig();
        }
    }

    public void SaveRiskConfig(RiskConfig config)
    {
        var json = JsonSerializer.Serialize(config);
        Preferences.Default.Set(RiskConfigKey, json);
    }

    #endregion

    #region 内部方法

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS strategies (
                    id TEXT PRIMARY KEY,
                    symbol TEXT NOT NULL,
                    type INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    side INTEGER NOT NULL,
                    trigger_price REAL NOT NULL,
                    stop_loss_price REAL,
                    take_profit_price REAL,
                    quantity REAL NOT NULL,
                    max_position_percent REAL,
                    custom_params TEXT,
                    created_at TEXT NOT NULL,
                    last_triggered_at TEXT,
                    execution_count INTEGER DEFAULT 0,
                    max_executions INTEGER
                );
                CREATE INDEX IF NOT EXISTS idx_strategies_symbol ON strategies(symbol);
                CREATE INDEX IF NOT EXISTS idx_strategies_status ON strategies(status);

                CREATE TABLE IF NOT EXISTS trade_records (
                    id TEXT PRIMARY KEY,
                    strategy_id TEXT NOT NULL,
                    symbol TEXT NOT NULL,
                    side INTEGER NOT NULL,
                    order_type INTEGER NOT NULL,
                    requested_qty REAL NOT NULL,
                    executed_qty REAL NOT NULL,
                    requested_price REAL,
                    executed_price REAL NOT NULL,
                    commission REAL DEFAULT 0,
                    commission_asset TEXT,
                    status INTEGER NOT NULL,
                    binance_order_id INTEGER,
                    ai_reasoning TEXT,
                    created_at TEXT NOT NULL,
                    completed_at TEXT,
                    FOREIGN KEY (strategy_id) REFERENCES strategies(id)
                );
                CREATE INDEX IF NOT EXISTS idx_records_strategy ON trade_records(strategy_id);
                CREATE INDEX IF NOT EXISTS idx_records_symbol ON trade_records(symbol);
                CREATE INDEX IF NOT EXISTS idx_records_created ON trade_records(created_at);

                CREATE TABLE IF NOT EXISTS daily_stats (
                    date TEXT PRIMARY KEY,
                    trade_count INTEGER DEFAULT 0,
                    total_pnl REAL DEFAULT 0,
                    total_commission REAL DEFAULT 0
                );
                """;
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("交易数据库初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化交易数据库失败");
        }
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
            BinanceOrderId = reader.GetInt64(reader.GetOrdinal("binance_order_id")),
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

    private static async Task<List<TradingStrategy>> ReadStrategiesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var strategies = new List<TradingStrategy>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            strategies.Add(ReadStrategy(reader));
        return strategies;
    }

    #endregion

    public void Dispose() => GC.SuppressFinalize(this);
}
