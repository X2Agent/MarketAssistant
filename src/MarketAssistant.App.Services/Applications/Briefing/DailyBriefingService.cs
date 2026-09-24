using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace MarketAssistant.Applications.Briefing;

/// <summary>
/// 每日 AI 简报服务实现：单次 LLM 调用生成，严格 Token 预算（自选/要闻各取前 N 条、
/// 内容截断、输出上限），按本地日期缓存。
/// 通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型。
/// </summary>
public sealed class DailyBriefingService : SqliteServiceBase, IDailyBriefingService
{
    /// <summary>纳入简报的自选数量上限（按异动幅度优先，控制输入 Token）。</summary>
    public const int MaxFavorites = 8;

    /// <summary>纳入简报的要闻条数上限。</summary>
    public const int MaxNewsItems = 8;

    /// <summary>单条要闻正文截断长度。</summary>
    public const int MaxNewsContentLength = 80;

    /// <summary>简报正文字数上限（超出截断，防止单次响应膨胀）。</summary>
    public const int MaxBriefingLength = 1200;

    private const int MaxOutputTokens = 800;

    private readonly MarketType _marketType;
    private readonly IFavoriteService _favoriteService;
    private readonly ITelegramService _telegramService;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<DailyBriefingService> _logger;

    public DailyBriefingService(
        [ServiceKey] MarketType marketType,
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        ILogger<DailyBriefingService> logger)
        : base(logger)
    {
        _marketType = marketType;
        _favoriteService = serviceProvider.GetRequiredKeyedService<IFavoriteService>(marketType);
        _telegramService = serviceProvider.GetRequiredKeyedService<ITelegramService>(marketType);
        _chatClientFactory = chatClientFactory;
        _logger = logger;
    }

    public async Task<DailyBriefing?> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        return await QueryAsync(TodayKey(), cancellationToken);
    }

    public async Task<DailyBriefing?> GenerateAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);

        if (!force)
        {
            var cached = await QueryAsync(TodayKey(), cancellationToken);
            if (cached != null)
            {
                _logger.LogInformation("每日简报命中当天缓存: {Market}, {DateKey}", _marketType, cached.DateKey);
                return cached;
            }
        }

        var favorites = await LoadFavoritesAsync(cancellationToken);
        var news = await LoadNewsAsync(cancellationToken);
        if (favorites.Count == 0 && news.Count == 0)
        {
            // 无可摘要内容不是错误：UI 引导用户先添加自选
            _logger.LogInformation("自选与要闻均为空，跳过简报生成: {Market}", _marketType);
            return null;
        }

        var runtime = _chatClientFactory.CreateRuntime();
        var prompt = BuildPrompt(favorites, news);

        var response = await runtime.Client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions
            {
                Temperature = 0.3f,
                MaxOutputTokens = MaxOutputTokens
            },
            cancellationToken);

        var content = response.Text?.Trim() ?? string.Empty;
        if (content.Length == 0)
        {
            throw new InvalidOperationException("简报生成失败：模型未返回有效内容");
        }

        if (content.Length > MaxBriefingLength)
        {
            content = content[..MaxBriefingLength];
        }

        var briefing = new DailyBriefing
        {
            MarketType = _marketType,
            DateKey = TodayKey(),
            Content = content,
            GeneratedAt = DateTime.UtcNow,
            FavoriteCount = favorites.Count,
            ModelId = runtime.ModelId
        };

        await SaveAsync(briefing, cancellationToken);
        _logger.LogInformation(
            "每日简报生成完成: {Market}, 自选 {FavoriteCount} 条, 要闻 {NewsCount} 条, 模型 {ModelId}",
            _marketType, favorites.Count, news.Count, runtime.ModelId);
        return briefing;
    }

    /// <summary>
    /// 自选行情按异动幅度优先纳入（静态等权排序，|涨跌幅| 降序），
    /// 无涨跌数据的排在最后；解析失败的条目保留但无涨跌排序权重。
    /// </summary>
    private async Task<List<AssetInfo>> LoadFavoritesAsync(CancellationToken cancellationToken)
    {
        var favorites = await _favoriteService.GetFavoritesWithLatestDataAsync(cancellationToken);
        return favorites
            .OrderByDescending(f => Math.Abs(ParsePercent(f.ChangePercentage)))
            .Take(MaxFavorites)
            .ToList();
    }

    private async Task<List<Telegram>> LoadNewsAsync(CancellationToken cancellationToken)
    {
        var news = await _telegramService.GetTelegraphsAsync(cancellationToken);
        return news.Take(MaxNewsItems).ToList();
    }

    private static decimal ParsePercent(string? percent)
        => decimal.TryParse(percent?.TrimEnd('%'), out var value) ? value : 0m;

    private static string TodayKey() => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// 构建受控提示词：只给模型脱敏后的结构化事实（无原始 HTML/JSON 噪声），
    /// 明确禁止编造与买入建议——简报定位是"要闻归因"，不是投资顾问。
    /// </summary>
    private static string BuildPrompt(IReadOnlyList<AssetInfo> favorites, IReadOnlyList<Telegram> news)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("你是一名冷静的盘前简报助手。请基于以下【自选行情】与【市场要闻】生成一份中文每日简报。");
        builder.AppendLine();
        builder.AppendLine("要求：");
        builder.AppendLine("1. 分两节：『隔夜要闻』（3-5 条要点，标注与哪只自选相关）与『异动归因』（逐只自选简述涨跌与最可能原因）；");
        builder.AppendLine("2. 只使用给定事实，禁止编造数据、价格或新闻；信息不足时如实说明『暂无相关信息』；");
        builder.AppendLine("3. 不做买卖建议、不预测点位，保持中性客观；");
        builder.AppendLine("4. 总计不超过 400 字，直接输出正文，不要寒暄与免责声明。");

        if (favorites.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("<favorites>");
            foreach (var favorite in favorites)
            {
                var change = string.IsNullOrWhiteSpace(favorite.ChangePercentage) ? "涨跌未知" : favorite.ChangePercentage;
                builder.AppendLine($"- {favorite.Name}（{favorite.Code}）：现价 {favorite.CurrentPrice}，{change}");
            }
            builder.AppendLine("</favorites>");
        }

        if (news.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("<news>");
            foreach (var item in news)
            {
                var content = item.Content ?? string.Empty;
                if (content.Length > MaxNewsContentLength)
                {
                    content = content[..MaxNewsContentLength] + "…";
                }
                builder.AppendLine($"- [{item.Time}] {item.Title}：{content}");
            }
            builder.AppendLine("</news>");
        }

        return builder.ToString();
    }

    private async Task<DailyBriefing?> QueryAsync(string dateKey, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT date_key, content, generated_at, favorite_count, model_id
            FROM briefings
            WHERE market_type = @market AND date_key = @dateKey
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@market", (int)_marketType);
        cmd.Parameters.AddWithValue("@dateKey", dateKey);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DailyBriefing
        {
            MarketType = _marketType,
            DateKey = reader.GetString(0),
            Content = reader.GetString(1),
            GeneratedAt = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            FavoriteCount = reader.GetInt32(3),
            ModelId = reader.GetString(4)
        };
    }

    private async Task SaveAsync(DailyBriefing briefing, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO briefings (market_type, date_key, content, generated_at, favorite_count, model_id)
            VALUES (@market, @dateKey, @content, @generatedAt, @favoriteCount, @modelId)
            ON CONFLICT(market_type, date_key) DO UPDATE SET
                content = excluded.content,
                generated_at = excluded.generated_at,
                favorite_count = excluded.favorite_count,
                model_id = excluded.model_id
            """;
        cmd.Parameters.AddWithValue("@market", (int)briefing.MarketType);
        cmd.Parameters.AddWithValue("@dateKey", briefing.DateKey);
        cmd.Parameters.AddWithValue("@content", briefing.Content);
        cmd.Parameters.AddWithValue("@generatedAt", briefing.GeneratedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@favoriteCount", briefing.FavoriteCount);
        cmd.Parameters.AddWithValue("@modelId", briefing.ModelId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override async Task InitializeDatabaseAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS briefings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                market_type INTEGER NOT NULL,
                date_key TEXT NOT NULL,
                content TEXT NOT NULL,
                generated_at TEXT NOT NULL,
                favorite_count INTEGER NOT NULL DEFAULT 0,
                model_id TEXT NOT NULL DEFAULT ''
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_briefings_market_date ON briefings(market_type, date_key);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
