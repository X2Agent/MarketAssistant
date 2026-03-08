using System.Globalization;
using System.Text.Json;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Archive;

/// <summary>
/// 分析报告存档服务，使用 SQLite 持久化历史报告
/// </summary>
public class ReportArchiveService : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<ReportArchiveService> _logger;
    private readonly Task _initializeTask;

    public ReportArchiveService(ILogger<ReportArchiveService> logger)
    {
        _logger = logger;

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName);
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "reports.db");
        _connectionString = $"Data Source={dbPath}";

        _initializeTask = InitializeDatabaseAsync();
    }

    /// <summary>
    /// 保存分析报告
    /// </summary>
    public async Task SaveAsync(MarketAnalysisReport report, CancellationToken cancellationToken = default)
    {
        try
        {
            await _initializeTask;
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO reports (asset_code, score, created_at, report_json)
                VALUES (@code, @score, @time, @json)
                """;
            cmd.Parameters.AddWithValue("@code", report.StockSymbol);
            cmd.Parameters.AddWithValue("@score", report.CoordinatorResult.OverallScore);
            cmd.Parameters.AddWithValue("@time", report.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(report));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存分析报告失败: {Asset}", report.StockSymbol);
        }
    }

    /// <summary>
    /// 获取某资产的历史报告摘要（按时间倒序）
    /// </summary>
    public async Task<List<ReportSummary>> GetSummariesAsync(
        string assetCode,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReportSummary>();
        try
        {
            await _initializeTask;
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, asset_code, score, created_at, report_json
                FROM reports WHERE asset_code = @code
                ORDER BY created_at DESC LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@code", assetCode);
            cmd.Parameters.AddWithValue("@limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new ReportSummary
                {
                    Id = reader.GetInt64(0),
                    AssetCode = reader.GetString(1),
                    Score = reader.GetDouble(2),
                    CreatedAt = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                });

                if (!reader.IsDBNull(4))
                {
                    TryFillDimensionScores(results[^1], reader.GetString(4));
                }
            }

            ApplyTimelineDeltas(results);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询报告摘要失败: {Asset}", assetCode);
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
            await _initializeTask;
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT report_json FROM reports WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            var json = await cmd.ExecuteScalarAsync(cancellationToken) as string;
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<MarketAnalysisReport>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载报告失败: id={Id}", id);
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
            await _initializeTask;
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM reports WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除报告失败: id={Id}", id);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        return conn;
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS reports (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_code TEXT NOT NULL,
                    score REAL NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    report_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_reports_asset ON reports(asset_code);
                CREATE INDEX IF NOT EXISTS idx_reports_time ON reports(created_at);
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化报告数据库失败");
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
        catch
        {
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 报告摘要（列表展示用，不含完整 JSON）
/// </summary>
public class ReportSummary
{
    public long Id { get; set; }
    public string AssetCode { get; set; } = string.Empty;
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
