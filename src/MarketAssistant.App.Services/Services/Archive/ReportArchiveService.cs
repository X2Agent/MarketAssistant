using System.Globalization;
using System.Text.Json;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Archive;

/// <summary>
/// 分析报告存档服务，使用 SQLite 持久化历史报告。
/// </summary>
public class ReportArchiveService(ILogger<ReportArchiveService> logger) : SqliteServiceBase(logger)
{

    /// <summary>
    /// 保存分析报告。失败时抛出异常，调用方可据此决定是否回滚缓存等后续操作。
    /// </summary>
    public async Task SaveAsync(MarketAnalysisReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO reports (asset_code, market_type, rating, score, created_at, report_json)
                VALUES (@code, @market, @rating, @score, @time, @json)
                """;
            cmd.Parameters.AddWithValue("@code", report.AssetSymbol);
            cmd.Parameters.AddWithValue("@market", report.MarketType.HasValue ? (int)report.MarketType.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@rating", (int)report.CoordinatorResult.InvestmentRating);
            cmd.Parameters.AddWithValue("@score", report.CoordinatorResult.OverallScore);
            cmd.Parameters.AddWithValue("@time", report.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(report));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 抛出异常而非吞并，让调用方感知归档失败（避免幽灵报告）
            throw new InvalidOperationException($"保存分析报告失败: {report.AssetSymbol}", ex);
        }
    }

    /// <summary>
    /// 获取某资产的历史报告摘要（按时间倒序）
    /// </summary>
    /// <param name="marketType">
    /// 可选市场过滤；为 null 时返回该标的的全部报告。历史报告的市场列为空，仅在未指定市场时可见。
    /// </param>
    public async Task<List<ReportSummary>> GetSummariesAsync(
        string assetCode,
        int limit = 20,
        CancellationToken cancellationToken = default,
        MarketType? marketType = null)
    {
        var results = new List<ReportSummary>();
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, asset_code, market_type, rating, score, created_at, report_json
                FROM reports WHERE asset_code = @code AND (@market IS NULL OR market_type = @market)
                ORDER BY created_at DESC LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@code", assetCode);
            cmd.Parameters.AddWithValue("@market", marketType.HasValue ? (int)marketType.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new ReportSummary
                {
                    Id = reader.GetInt64(0),
                    AssetCode = reader.GetString(1),
                    MarketType = reader.IsDBNull(2) ? null : (MarketType)reader.GetInt32(2),
                    Rating = reader.IsDBNull(3) ? null : (InvestmentRating)reader.GetInt32(3),
                    Score = reader.GetDouble(4),
                    CreatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                });

                if (!reader.IsDBNull(6))
                {
                    TryFillDimensionScores(results[^1], reader.GetString(6));
                }
            }

            ApplyTimelineDeltas(results);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "查询报告摘要失败: {Asset}", assetCode);
        }
        return results;
    }

    /// <summary>
    /// 根据 ID 加载完整报告
    /// </summary>
    public async Task<MarketAnalysisReport?> LoadAsync(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT report_json FROM reports WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            var json = await cmd.ExecuteScalarAsync(cancellationToken) as string;
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<MarketAnalysisReport>(json);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "加载报告失败: id={Id}", id);
            return null;
        }
    }

    /// <summary>
    /// 删除指定报告
    /// </summary>
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM reports WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "删除报告失败: id={Id}", id);
        }
    }

    protected override async Task InitializeDatabaseAsync()
    {
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS reports (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_code TEXT NOT NULL,
                    market_type INTEGER,
                    rating INTEGER,
                    score REAL NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    report_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_reports_asset ON reports(asset_code);
                CREATE INDEX IF NOT EXISTS idx_reports_time ON reports(created_at);
                """;
            await cmd.ExecuteNonQueryAsync();

            await EnsureMarketColumnsAsync(conn);

            await using var indexCmd = conn.CreateCommand();
            indexCmd.CommandText =
                "CREATE INDEX IF NOT EXISTS idx_reports_market ON reports(market_type, created_at);";
            await indexCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // 记录异常并重新抛出：让 _initializeTask 处于 Faulted 状态，
            // 后续 await _initializeTask 会抛出异常，调用方能感知数据库不可用
            Logger.LogError(ex, "初始化报告数据库失败");
            throw;
        }
    }

    /// <summary>
    /// 为既有 reports 表补齐后加列（旧库升级路径）。
    /// 历史行的 market_type/rating 保持 NULL，表示市场与评级未知，不做推断回填。
    /// </summary>
    private static async Task EnsureMarketColumnsAsync(SqliteConnection conn)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA table_info(reports)";
            await using var reader = await pragmaCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        foreach (var (columnName, columnDefinition) in new[]
                 {
                     ("market_type", "INTEGER"),
                     ("rating", "INTEGER")
                 })
        {
            if (existingColumns.Contains(columnName))
            {
                continue;
            }

            await using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE reports ADD COLUMN {columnName} {columnDefinition}";
            await alterCmd.ExecuteNonQueryAsync();
        }
    }

    private static void TryFillDimensionScores(ReportSummary summary, string reportJson)
    {
        try
        {
            var report = JsonSerializer.Deserialize<MarketAnalysisReport>(reportJson);
            var scores = report?.CoordinatorResult.DimensionScores;
            if (scores == null) return;

            summary.FundamentalScore = scores.Fundamental;
            summary.TechnicalScore = scores.Technical;
            summary.FinancialScore = scores.Financial;
            summary.SentimentScore = scores.Sentiment;
            summary.NewsScore = scores.News;
        }
        catch (JsonException)
        {
            // 反序列化失败时维度分数保持默认值 0，不影响摘要展示
        }
    }

    private static void ApplyTimelineDeltas(List<ReportSummary> summaries)
    {
        for (var i = 0; i < summaries.Count; i++)
        {
            if (i == summaries.Count - 1)
            {
                summaries[i].ScoreDelta = 0;
                summaries[i].FundamentalDelta = 0;
                summaries[i].TechnicalDelta = 0;
                summaries[i].FinancialDelta = 0;
                continue;
            }

            var current = summaries[i];
            var previous = summaries[i + 1];

            current.ScoreDelta = current.Score - previous.Score;
            current.FundamentalDelta = current.FundamentalScore - previous.FundamentalScore;
            current.TechnicalDelta = current.TechnicalScore - previous.TechnicalScore;
            current.FinancialDelta = current.FinancialScore - previous.FinancialScore;
        }
    }

}

/// <summary>
/// 报告摘要（列表展示用，不含完整 JSON）
/// </summary>
public class ReportSummary
{
    public long Id { get; set; }
    public string AssetCode { get; set; } = string.Empty;

    /// <summary>报告所属市场；该字段上线前归档的历史报告为 null（未知）。</summary>
    public MarketType? MarketType { get; set; }

    /// <summary>协调器给出的综合投资评级；历史报告为 null（未知）。</summary>
    public InvestmentRating? Rating { get; set; }

    public double Score { get; set; }
    public DateTime CreatedAt { get; set; }
    public double FundamentalScore { get; set; }
    public double TechnicalScore { get; set; }
    public double FinancialScore { get; set; }
    public double SentimentScore { get; set; }
    public double NewsScore { get; set; }

    public double ScoreDelta { get; set; }
    public double FundamentalDelta { get; set; }
    public double TechnicalDelta { get; set; }
    public double FinancialDelta { get; set; }

    public string ScoreTrendText => $"总分 {Score:F1} ({FormatDelta(ScoreDelta)})";
    public string KeyScoreChangeText =>
        $"基{FundamentalScore:F1}({FormatDelta(FundamentalDelta)}) 技{TechnicalScore:F1}({FormatDelta(TechnicalDelta)}) 财{FinancialScore:F1}({FormatDelta(FinancialDelta)})";

    private static string FormatDelta(double value)
    {
        return value > 0 ? $"+{value:F1}" : $"{value:F1}";
    }
}
