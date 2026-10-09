using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Settings;
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
public sealed class DailyBriefingService(
    [ServiceKey] MarketType marketType,
    IServiceProvider serviceProvider,
    IChatClientFactory chatClientFactory,
    IUserSettingService userSettingService,
    ILogger<DailyBriefingService> logger) : SqliteServiceBase(logger), IDailyBriefingService
{
    /// <summary>纳入简报的自选数量上限（按异动幅度优先，控制输入 Token）。</summary>
    public const int MaxFavorites = 8;

    /// <summary>
    /// 纳入简报的要闻条数上限。
    /// 数据源已按时间倒序返回约 28~29 小时（100 条）的快讯，此处取前 40 条入模，
    /// 既覆盖当日隔夜与开盘前消息，又避免超长上下文导致 Token 成本失控。
    /// 重要性与自选相关性交由模型判断，代码不做本地硬过滤（见 <c>BuildPrompt</c>）。
    /// </summary>
    public const int MaxNewsItems = 40;

    /// <summary>单条要闻标题截断长度（实测标题均值 20 字、最长 48 字，截断损失极小）。</summary>
    public const int MaxNewsTitleLength = 30;

    /// <summary>
    /// 单条要闻正文截断长度。实测摘要均值 102 字、最长 518 字，冗余较多，
    /// 配合 40 条总量可把要闻输入控制在约 2100 token。
    /// </summary>
    public const int MaxNewsContentLength = 50;

    /// <summary>简报正文字数上限（超出截断，防止单次响应膨胀）。</summary>
    public const int MaxBriefingLength = 1200;

    private const int MaxOutputTokens = 800;

    private readonly MarketType _marketType = marketType;
    private readonly IFavoriteService _favoriteService = serviceProvider.GetRequiredKeyedService<IFavoriteService>(marketType);
    private readonly ITelegramService _telegramService = serviceProvider.GetRequiredKeyedService<ITelegramService>(marketType);
    private readonly IChatClientFactory _chatClientFactory = chatClientFactory;
    private readonly IUserSettingService _userSettingService = userSettingService;
    private readonly ILogger<DailyBriefingService> _logger = logger;

    /// <summary>
    /// 归集本次简报用量的虚拟 Agent 名。简报是裸 <see cref="IChatClient"/> 调用（不经 MAF
    /// Agent 中间件），故不落入分析链路的分析师维度；用固定名以便与分析报告的
    /// Token 汇总并列展示，且不会与任何分析师重名。
    /// </summary>
    public const string TokenUsageAgentName = "DailyBriefing";

    public bool IsEnabled => _userSettingService.CurrentSetting.EnableDailyBriefing;

    public async Task<DailyBriefingUsageSummary> GetUsageSummaryAsync(
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        // 回溯窗口上限，避免误传超大值导致全表扫描
        const int MaxWindowDays = 365;
        var window = Math.Clamp(days, 1, MaxWindowDays);

        await EnsureInitializedAsync(InitializeDatabaseAsync);

        // 固定 InvariantCulture：date_key 是字典序即时序的比较键，不能跟随区域
        //（th-TH 佛历等区域会产出 2569-xx-xx 之类的年份，破坏窗口过滤）
        var fromDateKey = DateTime.Now.AddDays(-(window - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        // 按本地日期键做字符串比较即可：date_key 恒为 yyyy-MM-dd 定长零填充，字典序即时序。
        cmd.CommandText = """
            SELECT COUNT(*),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0),
                   COALESCE(MAX(token_estimated), 0)
            FROM briefings
            WHERE market_type = @market AND date_key >= @fromDateKey
            """;
        cmd.Parameters.AddWithValue("@market", (int)_marketType);
        cmd.Parameters.AddWithValue("@fromDateKey", fromDateKey);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DailyBriefingUsageSummary(0, 0, 0, false);
        }

        return new DailyBriefingUsageSummary(
            reader.GetInt32(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt32(3) != 0);
    }

    public async Task<DailyBriefing?> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        return await QueryAsync(TodayKey(), cancellationToken);
    }

    public async Task<DailyBriefing?> GenerateAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        // 用户开关门禁：简报会产生真实的模型调用与费用，关闭后连手动生成也不可用。
        if (!IsEnabled)
        {
            _logger.LogInformation("每日简报功能已由用户关闭，跳过生成: {Market}", _marketType);
            return null;
        }

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

        var requestMessage = new ChatMessage(ChatRole.User, prompt);
        var response = await runtime.Client.GetResponseAsync(
            [requestMessage],
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

        var (inputTokens, outputTokens, isPreciseUsage) = MeasureUsage(requestMessage, response);

        var briefing = new DailyBriefing
        {
            MarketType = _marketType,
            DateKey = TodayKey(),
            Content = content,
            GeneratedAt = DateTime.UtcNow,
            FavoriteCount = favorites.Count,
            ModelId = runtime.ModelId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Estimated = !isPreciseUsage
        };

        await SaveAsync(briefing, cancellationToken);
        _logger.LogInformation(
            "每日简报生成完成: {Market}, 自选 {FavoriteCount} 条, 要闻 {NewsCount} 条, 模型 {ModelId}, " +
            "输入 {InputTokens} tokens, 输出 {OutputTokens} tokens{UsageSource}",
            _marketType, favorites.Count, news.Count, runtime.ModelId,
            inputTokens, outputTokens,
            isPreciseUsage ? string.Empty : "（估算值）");
        return briefing;
    }

    /// <summary>
    /// 采集本次调用的 Token 用量：优先取提供商返回的精确 <see cref="UsageContent"/>，
    /// 缺失时回退到本地估算（与 <c>TokenTrackingMiddleware</c> 同一口径），
    /// 并回报到运行级记录器，使简报用量能与分析报告的 Token 汇总并列统计。
    /// </summary>
    private static (long Input, long Output, bool IsPrecise) MeasureUsage(
        ChatMessage requestMessage,
        ChatResponse response)
    {
        UsageDetails? usage = null;
        foreach (var message in response.Messages)
        {
            if (message.Contents?.OfType<UsageContent>().FirstOrDefault() is { } usageContent)
            {
                usage = usageContent.Details;
                break;
            }
        }

        var isPrecise = usage is not null;
        var inputTokens = usage?.InputTokenCount ?? TokenEstimator.EstimateTokens(requestMessage);
        var outputTokens = usage?.OutputTokenCount
                           ?? TokenEstimator.EstimateTokens(response.Text ?? string.Empty);
        return (inputTokens, outputTokens, isPrecise);
    }

    /// <summary>
    /// 自选行情按异动幅度优先纳入（静态等权排序，|涨跌幅| 降序），
    /// 无涨跌数据的排在最后；解析失败的条目保留但无涨跌排序权重。
    /// </summary>
    private async Task<List<AssetInfo>> LoadFavoritesAsync(CancellationToken cancellationToken)
    {
        var favorites = await _favoriteService.GetFavoritesWithLatestDataAsync(cancellationToken);
        return [.. favorites
            .OrderByDescending(f => Math.Abs(ParsePercent(f.ChangePercentage)))
            .Take(MaxFavorites)];
    }

    /// <summary>
    /// 取要闻入模。数据源已按时间倒序返回约 28~29 小时（100 条）的快讯，
    /// 此处只做条数截断，**不做本地重要性或相关性过滤**——
    /// 实测 A 股 <c>import</c> 字段 97% 为 0（无区分度）、88% 的快讯无关联股票，
    /// 基于这些信号做硬过滤等于用噪声决策，故交由模型自行判断。
    /// </summary>
    private async Task<List<Telegram>> LoadNewsAsync(CancellationToken cancellationToken)
    {
        var news = await _telegramService.GetTelegraphsAsync(cancellationToken);
        return [.. news.Take(MaxNewsItems)];
    }

    private static decimal ParsePercent(string? percent)
        => decimal.TryParse(percent?.TrimEnd('%'), out var value) ? value : 0m;

    // 当天缓存键，与 briefings.date_key 的写入/查询共用同一口径；固定 InvariantCulture，
    // 与 GetUsageSummaryAsync 的窗口过滤保持一致（不能跟随区域日历）
    private static string TodayKey() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 构建受控提示词：只给模型脱敏后的结构化事实（无原始 HTML/JSON 噪声），
    /// 明确禁止编造与买入建议——简报定位是"要闻归因"，不是投资顾问。
    /// </summary>
    private static string BuildPrompt(IReadOnlyList<AssetInfo> favorites, IReadOnlyList<Telegram> news)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("你是一名冷静的市场简报助手。请基于以下【自选行情】与【市场要闻】生成一份中文每日简报。");
        builder.AppendLine();
        builder.AppendLine("要求：");
        builder.AppendLine("1. 分三节：『要闻速览』（挑出其中真正重要的要点，与自选相关的须点明自选名称）、"
            + "『自选异动归因』（逐只自选简述涨跌与最可能原因）、『数据说明』（快讯若未覆盖任何自选或无值得提及的内容，如实说明即可）；");
        builder.AppendLine("2. 快讯按时间倒序给出、覆盖最近约一天，但绝大多数并无价值——请自行判断重要性，"
            + "宁可少写也不要凑数，不要因为某条快讯排在前面就认为它重要；");
        builder.AppendLine("3. 只使用给定事实，禁止编造数据、价格或新闻；无法判断因果时如实说明『暂无相关信息』，不要强行归因；");
        builder.AppendLine("4. 不做买卖建议、不预测点位，保持中性客观；");
        builder.AppendLine("5. 总计不超过 400 字，直接输出正文，不要寒暄与免责声明。");

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
                var title = item.Title ?? string.Empty;
                if (title.Length > MaxNewsTitleLength)
                {
                    title = title[..MaxNewsTitleLength] + "…";
                }

                var content = item.Content ?? string.Empty;
                if (content.Length > MaxNewsContentLength)
                {
                    content = content[..MaxNewsContentLength] + "…";
                }

                builder.AppendLine($"- [{item.Time}] {title}：{content}");
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
            SELECT date_key, content, generated_at, favorite_count, model_id,
                   input_tokens, output_tokens, token_estimated
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
            ModelId = reader.GetString(4),
            InputTokens = reader.GetInt64(5),
            OutputTokens = reader.GetInt64(6),
            Estimated = reader.GetInt32(7) != 0
        };
    }

    private async Task SaveAsync(DailyBriefing briefing, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO briefings (market_type, date_key, content, generated_at, favorite_count, model_id,
                                   input_tokens, output_tokens, token_estimated)
            VALUES (@market, @dateKey, @content, @generatedAt, @favoriteCount, @modelId,
                    @inputTokens, @outputTokens, @tokenEstimated)
            ON CONFLICT(market_type, date_key) DO UPDATE SET
                content = excluded.content,
                generated_at = excluded.generated_at,
                favorite_count = excluded.favorite_count,
                model_id = excluded.model_id,
                input_tokens = excluded.input_tokens,
                output_tokens = excluded.output_tokens,
                token_estimated = excluded.token_estimated
            """;
        cmd.Parameters.AddWithValue("@market", (int)briefing.MarketType);
        cmd.Parameters.AddWithValue("@dateKey", briefing.DateKey);
        cmd.Parameters.AddWithValue("@content", briefing.Content);
        cmd.Parameters.AddWithValue("@generatedAt", briefing.GeneratedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@favoriteCount", briefing.FavoriteCount);
        cmd.Parameters.AddWithValue("@modelId", briefing.ModelId);
        cmd.Parameters.AddWithValue("@inputTokens", briefing.InputTokens);
        cmd.Parameters.AddWithValue("@outputTokens", briefing.OutputTokens);
        cmd.Parameters.AddWithValue("@tokenEstimated", briefing.Estimated ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override async Task InitializeDatabaseAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await MigrateSchemaAsync(conn);
    }

    /// <summary>
    /// 建表并执行旧库迁移（补齐 v1.0 之后新增的列）。
    /// </summary>
    /// <remarks>
    /// 独立成静态方法且接收 <see cref="SqliteConnection"/>，是为了让迁移逻辑
    /// 可用内存数据库单测覆盖——迁移一旦回归，升级用户的简报会永久不可用，
    /// 而这类问题在运行时才暴露，代价很高。
    /// 参照 <c>TradingDataService.MigrateDatabaseSchemaAsync</c> 的既有做法。
    /// </remarks>
    /// <param name="conn">已打开的连接。</param>
    internal static async Task MigrateSchemaAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS briefings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                market_type INTEGER NOT NULL,
                date_key TEXT NOT NULL,
                content TEXT NOT NULL,
                generated_at TEXT NOT NULL,
                favorite_count INTEGER NOT NULL DEFAULT 0,
                model_id TEXT NOT NULL DEFAULT '',
                input_tokens INTEGER NOT NULL DEFAULT 0,
                output_tokens INTEGER NOT NULL DEFAULT 0,
                token_estimated INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_briefings_market_date ON briefings(market_type, date_key);
            """;
        await cmd.ExecuteNonQueryAsync();

        // 旧库迁移：v1.0 之前的 briefings 表无 Token 用量列。
        // CREATE TABLE IF NOT EXISTS 不会为已存在的表补列，必须逐列探测后 ALTER，
        // 否则升级用户的 QueryAsync 会因列不存在而整条失败（简报永久不可用）。
        await using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA table_info(briefings);";
        await using var reader = await pragmaCmd.ExecuteReaderAsync();

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1));
        }
        await reader.DisposeAsync();

        var newColumns = new (string Column, string Definition)[]
        {
            ("input_tokens", "INTEGER NOT NULL DEFAULT 0"),
            ("output_tokens", "INTEGER NOT NULL DEFAULT 0"),
            ("token_estimated", "INTEGER NOT NULL DEFAULT 0")
        };

        foreach (var (column, definition) in newColumns)
        {
            if (existingColumns.Contains(column))
            {
                continue;
            }

            await using var alterCmd = conn.CreateCommand();
            // 列名来自上方常量表，非外部输入，不存在注入风险
            alterCmd.CommandText = $"ALTER TABLE briefings ADD COLUMN {column} {definition};";
            await alterCmd.ExecuteNonQueryAsync();
        }
    }
}
