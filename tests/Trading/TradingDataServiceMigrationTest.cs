using MarketAssistant.Services.Trading;
using Microsoft.Data.Sqlite;

namespace TestMarketAssistant.Trading;

[TestClass]
public sealed class TradingDataServiceMigrationTest
{
    private const string LiveSpotEnvironment = "crypto-live-spot";

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MigrateDatabaseSchemaAsync_MoneyColumns_ConvertedToTextWithoutPrecisionLoss()
    {
        await using var connection = await CreateLegacyDatabaseAsync();

        await TradingDataService.MigrateDatabaseSchemaAsync(connection);

        // 金额列类型应为 TEXT
        await AssertColumnTypeAsync(connection, "strategies", "trigger_price", "TEXT");
        await AssertColumnTypeAsync(connection, "trade_records", "executed_price", "TEXT");
        await AssertColumnTypeAsync(connection, "positions", "entry_price", "TEXT");
        await AssertColumnTypeAsync(connection, "daily_stats", "total_pnl", "TEXT");
        await AssertColumnTypeAsync(connection, "account_snapshots", "total_value_usdt", "TEXT");

        // 历史 REAL 值搬运后数值不变（CAST(REAL AS TEXT) 可能保留 ".0" 形式，按数值等价断言）
        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM strategies WHERE id = 'strategy-1' AND ABS(CAST(trigger_price AS REAL) - 65000) < 1e-9", 1L);
        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM trade_records WHERE id = 'record-1' AND ABS(CAST(executed_price AS REAL) - 65000) < 1e-9", 1L);
        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM daily_stats WHERE date = '2026-08-03' AND ABS(CAST(total_pnl AS REAL) - 120.5) < 1e-9", 1L);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MigrateDatabaseSchemaAsync_FromLegacySchema_PreservesDataAndCreatesEnvironmentIndexes()
    {
        await using var connection = await CreateLegacyDatabaseAsync();

        await TradingDataService.MigrateDatabaseSchemaAsync(connection);

        await AssertColumnExistsAsync(connection, "strategies", "environment");
        await AssertColumnExistsAsync(connection, "trade_records", "environment");
        await AssertColumnExistsAsync(connection, "positions", "environment");
        await AssertColumnExistsAsync(connection, "daily_stats", "environment");
        await AssertColumnExistsAsync(connection, "account_snapshots", "environment");
        await AssertColumnExistsAsync(connection, "risk_config", "environment");

        await AssertRowValueAsync(connection, "SELECT environment FROM strategies WHERE id = 'strategy-1'", LiveSpotEnvironment);
        await AssertRowValueAsync(connection, "SELECT environment FROM trade_records WHERE id = 'record-1'", LiveSpotEnvironment);
        await AssertRowValueAsync(connection, "SELECT environment FROM positions WHERE id = 'position-1'", LiveSpotEnvironment);
        await AssertRowValueAsync(connection, "SELECT environment FROM daily_stats WHERE date = '2026-08-03'", LiveSpotEnvironment);
        await AssertRowValueAsync(connection, "SELECT environment FROM account_snapshots WHERE date = '2026-08-03'", LiveSpotEnvironment);
        await AssertRowValueAsync(connection, "SELECT environment FROM risk_config WHERE market_type = 1", LiveSpotEnvironment);

        await AssertIndexExistsAsync(connection, "idx_strategies_environment_status");
        await AssertIndexExistsAsync(connection, "idx_records_environment_created");
        await AssertIndexExistsAsync(connection, "idx_positions_environment_symbol");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MigrateDatabaseSchemaAsync_WhenRunTwice_RemainsIdempotent()
    {
        await using var connection = await CreateLegacyDatabaseAsync();

        await TradingDataService.MigrateDatabaseSchemaAsync(connection);
        await TradingDataService.MigrateDatabaseSchemaAsync(connection);

        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM strategies", 1L);
        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM daily_stats", 1L);
        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM account_snapshots", 1L);
        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM risk_config", 1L);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MigrateDatabaseSchemaAsync_WhenMigrationFails_RollsBackAllSchemaChanges()
    {
        await using var connection = await CreateLegacyDatabaseAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DROP TABLE risk_config;
                CREATE TABLE risk_config (
                    market_type INTEGER PRIMARY KEY,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO risk_config (market_type, updated_at)
                VALUES (1, '2026-08-03T08:03:00.0000000Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsExactlyAsync<SqliteException>(
            () => TradingDataService.MigrateDatabaseSchemaAsync(connection));

        await AssertColumnDoesNotExistAsync(connection, "strategies", "environment");
        await AssertColumnDoesNotExistAsync(connection, "daily_stats", "environment");
        await AssertColumnDoesNotExistAsync(connection, "risk_config", "environment");
        await AssertRowValueAsync(connection, "SELECT COUNT(*) FROM daily_stats", 1L);
    }

    private static async Task<SqliteConnection> CreateLegacyDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = OFF;

            CREATE TABLE strategies (
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
                max_executions INTEGER,
                trailing_peak_price REAL
            );

            CREATE TABLE trade_records (
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
                completed_at TEXT
            );

            CREATE TABLE daily_stats (
                date TEXT PRIMARY KEY,
                trade_count INTEGER DEFAULT 0,
                total_pnl REAL DEFAULT 0,
                total_commission REAL DEFAULT 0
            );

            CREATE TABLE positions (
                id TEXT PRIMARY KEY,
                symbol TEXT NOT NULL,
                side INTEGER NOT NULL,
                quantity REAL NOT NULL,
                entry_price REAL NOT NULL,
                closed_quantity REAL DEFAULT 0,
                strategy_id TEXT,
                opened_at TEXT NOT NULL
            );

            CREATE TABLE account_snapshots (
                date TEXT PRIMARY KEY,
                total_value_usdt REAL NOT NULL,
                snapshot_at TEXT NOT NULL
            );

            CREATE TABLE risk_config (
                market_type INTEGER PRIMARY KEY,
                config_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            INSERT INTO strategies
                (id, symbol, type, status, side, trigger_price, quantity, created_at, execution_count)
            VALUES
                ('strategy-1', 'BTCUSDT', 0, 1, 0, 65000, 0.1, '2026-08-03T08:00:00.0000000Z', 0);

            INSERT INTO trade_records
                (id, strategy_id, symbol, side, order_type, requested_qty, executed_qty, executed_price, status, created_at)
            VALUES
                ('record-1', 'strategy-1', 'BTCUSDT', 0, 0, 0.1, 0.1, 65000, 1, '2026-08-03T08:01:00.0000000Z');

            INSERT INTO daily_stats (date, trade_count, total_pnl, total_commission)
            VALUES ('2026-08-03', 1, 120.5, 0.5);

            INSERT INTO positions
                (id, symbol, side, quantity, entry_price, closed_quantity, strategy_id, opened_at)
            VALUES
                ('position-1', 'BTCUSDT', 0, 0.1, 65000, 0, 'strategy-1', '2026-08-03T08:01:00.0000000Z');

            INSERT INTO account_snapshots (date, total_value_usdt, snapshot_at)
            VALUES ('2026-08-03', 10120.5, '2026-08-03T08:02:00.0000000Z');

            INSERT INTO risk_config (market_type, config_json, updated_at)
            VALUES (1, '{}', '2026-08-03T08:03:00.0000000Z');
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task AssertColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        Assert.Fail($"表 {tableName} 缺少列 {columnName}");
    }

    private static async Task AssertColumnDoesNotExistAsync(
        SqliteConnection connection,
        string tableName,
        string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.AreNotEqual(columnName, reader.GetString(1), ignoreCase: true);
        }
    }

    private static async Task AssertIndexExistsAsync(SqliteConnection connection, string indexName)
    {
        await AssertRowValueAsync(
            connection,
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{indexName}'",
            1L);
    }

    private static async Task AssertColumnTypeAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string expectedType)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                Assert.AreEqual(expectedType, reader.GetString(2), ignoreCase: true);
                return;
            }
        }

        Assert.Fail($"表 {tableName} 缺少列 {columnName}");
    }

    private static async Task AssertRowValueAsync(
        SqliteConnection connection,
        string commandText,
        object expected)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var actual = await command.ExecuteScalarAsync();
        Assert.AreEqual(expected, actual);
    }
}
