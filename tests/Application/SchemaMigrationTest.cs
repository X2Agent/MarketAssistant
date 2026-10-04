using MarketAssistant.Applications.Briefing;
using MarketAssistant.Services.Archive;
using Microsoft.Data.Sqlite;

namespace TestMarketAssistant.Application;

/// <summary>
/// 数据库迁移回归测试：用内存 SQLite 模拟"旧结构库 → 新版本"的升级过程。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须测：</b><c>CREATE TABLE IF NOT EXISTS</c> 遇到已存在的表会静默跳过，
/// 不会补列。因此新版本代码若直接 <c>SELECT 新列</c>，在从旧版升级的用户上会抛
/// <c>no such column</c>——该功能对老用户永久不可用，且只在真实升级场景暴露，
/// 开发与测试环境（每次都是全新库）完全测不到。
/// </para>
/// <para>
/// 这类故障不会报错退出，而是<b>静默失效</b>：例如 <c>reports.market_type</c> 缺失时
/// M3 回测无法按市场分基准，却仍能跑出结果，只是数字全错。因此迁移必须被测试守住。
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaMigrationTest
{
    /// <summary>
    /// v1.0 之前的 briefings 表定义（无 Token 用量列）。
    /// 与 <see cref="DailyBriefingService"/> 的 CREATE TABLE 保持历史一致。
    /// </summary>
    private const string LegacyBriefingsSchema = """
        CREATE TABLE briefings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            market_type INTEGER NOT NULL,
            date_key TEXT NOT NULL,
            content TEXT NOT NULL,
            generated_at TEXT NOT NULL,
            favorite_count INTEGER NOT NULL DEFAULT 0,
            model_id TEXT NOT NULL DEFAULT ''
        );
        """;

    /// <summary>
    /// 旧库中已存在的一条数据。迁移<b>必须保留</b>它——
    /// 补列操作若误用重建表（drop & create），用户历史简报就会全部丢失。
    /// </summary>
    private const string LegacyContent = "隔夜要闻：旧版本生成的简报";

    private static async Task<SqliteConnection> CreateLegacyDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var create = connection.CreateCommand();
        create.CommandText = LegacyBriefingsSchema;
        await create.ExecuteNonQueryAsync();

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO briefings
                (market_type, date_key, content, generated_at, favorite_count, model_id)
            VALUES (0, '2026-09-01', @content, '2026-09-01T08:00:00.0000000Z', 3, 'old-model');
            """;
        insert.Parameters.AddWithValue("@content", LegacyContent);
        await insert.ExecuteNonQueryAsync();

        return connection;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MigrateSchemaAsync_LegacyDatabase_ShouldAddTokenColumns()
    {
        await using var connection = await CreateLegacyDatabaseAsync();

        // 迁移前：确认模拟的确实是旧结构
        var before = await GetColumnsAsync(connection, "briefings");
        Assert.IsFalse(before.Contains("input_tokens"), "前置条件：旧库不应已有 input_tokens");

        await DailyBriefingService.MigrateSchemaAsync(connection);

        var after = await GetColumnsAsync(connection, "briefings");
        Assert.IsTrue(after.Contains("input_tokens"), "迁移后应补齐 input_tokens");
        Assert.IsTrue(after.Contains("output_tokens"), "迁移后应补齐 output_tokens");
        Assert.IsTrue(after.Contains("token_estimated"), "迁移后应补齐 token_estimated");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MigrateSchemaAsync_LegacyDatabase_ShouldPreserveExistingRows()
    {
        await using var connection = await CreateLegacyDatabaseAsync();

        await DailyBriefingService.MigrateSchemaAsync(connection);

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT content, input_tokens, output_tokens FROM briefings WHERE date_key = '2026-09-01';";
        await using var reader = await read.ExecuteReaderAsync();

        Assert.IsTrue(await reader.ReadAsync(), "旧数据行不应在迁移中丢失");
        Assert.AreEqual(LegacyContent, reader.GetString(0), "历史简报内容必须原样保留");

        // 新列对旧行应回落到默认值 0，而非 NULL——否则累加求和会得到 NULL
        Assert.AreEqual(0L, reader.GetInt64(1), "旧行的 input_tokens 应为默认值 0");
        Assert.AreEqual(0L, reader.GetInt64(2), "旧行的 output_tokens 应为默认值 0");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MigrateSchemaAsync_NewDatabase_ShouldCreateTableWithAllColumns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await DailyBriefingService.MigrateSchemaAsync(connection);

        var columns = await GetColumnsAsync(connection, "briefings");
        foreach (var expected in new[]
                 {
                     "id", "market_type", "date_key", "content", "generated_at",
                     "favorite_count", "model_id", "input_tokens", "output_tokens", "token_estimated"
                 })
        {
            Assert.IsTrue(columns.Contains(expected), $"全新库应包含列 {expected}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MigrateSchemaAsync_ShouldBeIdempotent()
    {
        // 初始化可能被并发触发或重复调用，迁移必须可重入
        await using var connection = await CreateLegacyDatabaseAsync();

        await DailyBriefingService.MigrateSchemaAsync(connection);
        await DailyBriefingService.MigrateSchemaAsync(connection);
        await DailyBriefingService.MigrateSchemaAsync(connection);

        var columns = await GetColumnsAsync(connection, "briefings");
        Assert.IsTrue(columns.Contains("input_tokens"), "重复迁移后列应仍然存在且唯一");

        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM briefings;";
        var rows = Convert.ToInt64(await count.ExecuteScalarAsync());
        Assert.AreEqual(1L, rows, "重复迁移不应产生额外数据行");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MigrateSchemaAsync_ShouldCreateUniqueIndexOnMarketAndDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await DailyBriefingService.MigrateSchemaAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO briefings
                (market_type, date_key, content, generated_at, favorite_count, model_id)
            VALUES (0, '2026-09-01', 'a', '2026-09-01T08:00:00.0000000Z', 0, 'm');
            """;
        await command.ExecuteNonQueryAsync();

        await using var duplicate = connection.CreateCommand();
        duplicate.CommandText = """
            INSERT INTO briefings
                (market_type, date_key, content, generated_at, favorite_count, model_id)
            VALUES (0, '2026-09-01', 'b', '2026-09-01T09:00:00.0000000Z', 0, 'm');
            """;

        // 同市场同日期只应有一条记录；缺唯一索引会让当天缓存被反复覆盖
        await Assert.ThrowsAsync<SqliteException>(async () => await duplicate.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// v1.0 之前的 reports 表定义：无 market_type / rating。
    /// </summary>
    private const string LegacyReportsSchema = """
        CREATE TABLE reports (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            asset_code TEXT NOT NULL,
            score REAL NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            report_json TEXT NOT NULL
        );
        """;

    private static async Task<SqliteConnection> CreateLegacyReportsDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var create = connection.CreateCommand();
        create.CommandText = LegacyReportsSchema;
        await create.ExecuteNonQueryAsync();

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO reports (asset_code, score, created_at, report_json)
            VALUES ('600519', 78.5, '2026-09-01T08:00:00.0000000Z', '{}');
            """;
        await insert.ExecuteNonQueryAsync();

        return connection;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ReportsMigration_LegacyDatabase_ShouldAddMarketAndRatingColumns()
    {
        await using var connection = await CreateLegacyReportsDatabaseAsync();

        var before = await GetColumnsAsync(connection, "reports");
        Assert.IsFalse(before.Contains("market_type"), "前置条件：旧库不应已有 market_type");

        await ReportArchiveService.EnsureMarketColumnsAsync(connection);

        var after = await GetColumnsAsync(connection, "reports");
        Assert.IsTrue(after.Contains("market_type"), "迁移后应补齐 market_type（M3 分市场基准依赖它）");
        Assert.IsTrue(after.Contains("rating"), "迁移后应补齐 rating");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ReportsMigration_LegacyRows_ShouldRemainNull_NotInferredFromAssetCode()
    {
        await using var connection = await CreateLegacyReportsDatabaseAsync();

        await ReportArchiveService.EnsureMarketColumnsAsync(connection);

        await using var read = connection.CreateCommand();
        // 关键断言：market_type 必须为 NULL。
        // 若有人日后"顺手加个回填"用 asset_code 前缀推断市场（sh/sz → A股），
        // A 股代码可能被误判，污染 M3 的分市场胜率统计且不报错。
        read.CommandText = "SELECT market_type, rating FROM reports WHERE asset_code = '600519';";
        await using var reader = await read.ExecuteReaderAsync();

        Assert.IsTrue(await reader.ReadAsync(), "旧报告行不应丢失");
        Assert.IsTrue(reader.IsDBNull(0), "历史行 market_type 必须保持 NULL，禁止由 asset_code 反推");
        Assert.IsTrue(reader.IsDBNull(1), "历史行 rating 必须保持 NULL");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ReportsMigration_ShouldBeIdempotent()
    {
        await using var connection = await CreateLegacyReportsDatabaseAsync();

        await ReportArchiveService.EnsureMarketColumnsAsync(connection);
        await ReportArchiveService.EnsureMarketColumnsAsync(connection);

        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM reports;";
        var rows = Convert.ToInt64(await count.ExecuteScalarAsync());
        Assert.AreEqual(1L, rows, "重复迁移不应影响既有数据行");
    }
}
