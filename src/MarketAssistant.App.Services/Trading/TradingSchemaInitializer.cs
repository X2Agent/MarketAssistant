using MarketAssistant.Infrastructure.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易数据库 schema 初始化与迁移（internal，单一职责：建表 + 历史结构迁移 + 索引创建）。
/// 由 <see cref="TradingDataService"/> 组合，各仓储通过 <see cref="TradingRepositoryBase"/> 复用其连接与初始化能力。
/// </summary>
internal sealed class TradingSchemaInitializer : SqliteServiceBase
{
    public TradingSchemaInitializer(ILogger logger) : base(logger)
    {
    }

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
                order_type INTEGER NOT NULL DEFAULT 0,
                slippage_tolerance TEXT,
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

    /// <summary>金额/数量列迁移计划：这些列在新 schema 中以 TEXT（十进制字符串）存储。</summary>
    private static readonly (string Table, string[] MoneyColumns)[] MoneyColumnMigrations =
    [
        ("strategies", ["trigger_price", "stop_loss_price", "take_profit_price", "quantity", "max_position_percent", "trailing_peak_price"]),
        ("trade_records", ["requested_qty", "executed_qty", "requested_price", "executed_price", "commission"]),
        ("daily_stats", ["total_pnl", "total_commission"]),
        ("positions", ["quantity", "entry_price", "closed_quantity"]),
        ("account_snapshots", ["total_value_usdt"])
    ];

    /// <summary>
    /// 确保交易数据库 schema 已初始化（幂等，延迟执行，失败可重试）。
    /// </summary>
    public Task EnsureSchemaInitializedAsync() => EnsureInitializedAsync(InitializeDatabaseAsync);

    /// <summary>
    /// 打开一个新的 SQLite 连接（WAL + busy_timeout），供各仓储使用。
    /// </summary>
    public Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        => OpenConnectionAsync(cancellationToken);

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
    private static async Task MigrateMoneyColumnsToTextAsync(SqliteConnection conn, SqliteTransaction transaction)
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

    private static async Task EnsureEnvironmentSchemaAsync(
        SqliteConnection conn,
        SqliteTransaction transaction)
    {
        await EnsureColumnAsync(conn, transaction, "strategies", "environment", $"TEXT NOT NULL DEFAULT '{TradingEnvironmentKeys.LiveSpot}'").ConfigureAwait(false);
        await EnsureColumnAsync(conn, transaction, "trade_records", "environment", $"TEXT NOT NULL DEFAULT '{TradingEnvironmentKeys.LiveSpot}'").ConfigureAwait(false);
        await EnsureColumnAsync(conn, transaction, "positions", "environment", $"TEXT NOT NULL DEFAULT '{TradingEnvironmentKeys.LiveSpot}'").ConfigureAwait(false);
        // 滑点容忍度以 TEXT（十进制字符串）存储，与金额列存储策略一致；
        // 历史库补充列时给出默认值，保证旧行读回为 Market / 0.003 的既有行为
        await EnsureColumnAsync(conn, transaction, "strategies", "order_type", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await EnsureColumnAsync(conn, transaction, "strategies", "slippage_tolerance", "TEXT DEFAULT '0.003'").ConfigureAwait(false);
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
            SELECT '{TradingEnvironmentKeys.LiveSpot}', date, trade_count, total_pnl, total_commission
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
            SELECT '{TradingEnvironmentKeys.LiveSpot}', date, total_value_usdt, snapshot_at
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
            SELECT '{TradingEnvironmentKeys.LiveSpot}', market_type, config_json, updated_at
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
}
