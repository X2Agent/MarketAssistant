using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Briefing;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Application;

/// <summary>
/// <see cref="DailyBriefingService"/> 单元测试：当天缓存复用、强制刷新、
/// 空输入跳过、Token 预算边界（自选/要闻条数、正文截断）与提示词内容。
/// 直连 market.db 清理测试产生的简报行（内容以 StubReplyContent 前缀标识），
/// 避免污染用户真实数据。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DailyBriefingServiceTest
{
    private const string StubReplyContent = "TESTBRIEF 隔夜要闻：测试。";
    private const string StubModelId = "stub-model";

    private static List<ChatMessage> _capturedRequests = [];
    private static int _requestCount;

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _requestCount);
            _capturedRequests.Add(messages.Last());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, StubReplyContent)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class StubChatClientFactory : IChatClientFactory
    {
        private readonly StubChatClient _client = new();

        public IChatClient CreateClient() => _client;

        public ChatClientRuntime CreateRuntime(string? modelIdOverride = null)
            => new(_client, "stub", StubModelId, "https://stub.local", "fingerprint", StructuredOutputMode.Text);

        public void Dispose() { }
    }

    private sealed class StubFavoriteService(List<AssetInfo> favorites) : IFavoriteService
    {
        public Task AddFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> IsFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<List<FavoriteAsset>> GetFavoritesCodesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<FavoriteAsset>());

        public Task<List<AssetInfo>> GetFavoritesWithLatestDataAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(favorites);

        public Task ClearFavoritesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubTelegramService(List<Telegram> news) : ITelegramService
    {
        public Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(news);
    }

    private static AssetInfo CreateFavorite(string name, string code, string changePercent)
        => new() { Name = name, Code = code, CurrentPrice = "10.00", ChangePercentage = changePercent };

    private static Telegram CreateNews(string title, string content = "内容")
        => new() { Time = "08:30", Title = title, Content = content };

    /// <summary>
    /// 最小可用的设置服务桩：只提供简报开关与服务商 ID，
    /// 其余成员不被本测试触达。
    /// </summary>
    private sealed class StubUserSettingService(bool enable = true) : IUserSettingService
    {
        public UserSetting CurrentSetting { get; } = new()
        {
            EnableDailyBriefing = enable,
        };

        public void LoadSettings() { }

        public void SaveSettings() { }

        public void UpdateSettings(UserSetting setting) { }

        public void UpdateSetting(Action<UserSetting> mutate) => mutate(CurrentSetting);

        public void ResetSettings() { }
    }

    /// <summary>
    /// A 股端到端：自选（新浪行情格式）+ 要闻（同花顺格式）→ 生成 → 内容可读。
    /// 锁定 A 股真实数据形态下的展示链路，避免只验证桩数据。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_AShare_WithRealDataShapes_ShouldProduceReadableBriefing()
    {
        // A 股自选：ChangePercentage 带显式正号（AShareAssetInfoService 写入格式）
        var favorites = new List<AssetInfo>
        {
            CreateFavorite("贵州茅台", "600519", "+1.50%"),
            CreateFavorite("宁德时代", "300750", "-2.30%")
        };
        // 要闻：同花顺快讯形态（Time 为 HH:mm:ss，含正文）
        var news = new List<Telegram>
        {
            new() { Time = "08:30", Title = "央行公开市场操作", Content = "今日开展逆回购操作，维护流动性合理充裕。" }
        };

        var service = CreateService(favorites, news, MarketType.AShare);

        var briefing = await service.GenerateAsync(force: true);

        Assert.IsNotNull(briefing, "A 股在自选与要闻齐备时应能生成简报");
        Assert.AreEqual(MarketType.AShare, briefing.MarketType, "简报市场类型必须为 A 股");
        Assert.IsFalse(string.IsNullOrWhiteSpace(briefing.Content), "简报正文不应为空");
        Assert.AreEqual(favorites.Count, briefing.FavoriteCount, "自选数量应被正确记录");

        // 提示词必须真正带上 A 股标的信息，LLM 才有素材可用
        var prompt = _capturedRequests[0].Text ?? string.Empty;
        StringAssert.Contains(prompt, "600519");
        StringAssert.Contains(prompt, "300750");
        StringAssert.Contains(prompt, "央行公开市场操作");
        StringAssert.Contains(prompt, "+1.50%", "A 股带正号的涨跌幅须原样传入提示词");
    }

    /// <summary>
    /// A 股无自选但有要闻时仍应生成（要闻是独立素材来源），
    /// 避免"未加自选"被误判为无内容。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_AShare_NewsOnlyWithoutFavorites_ShouldStillGenerate()
    {
        var news = new List<Telegram>
        {
            new() { Time = "09:15", Title = "两市开盘", Content = "沪指小幅高开。" }
        };
        var service = CreateService([], news, MarketType.AShare);

        var briefing = await service.GenerateAsync(force: true);

        Assert.IsNotNull(briefing, "有要闻即应生成，不应因无自选而跳过");
        var prompt = _capturedRequests[0].Text ?? string.Empty;
        StringAssert.Contains(prompt, "两市开盘");
        Assert.IsFalse(prompt.Contains("<favorites>"), "无自选时不应输出空的自选区块");
    }

    private static DailyBriefingService CreateService(
        List<AssetInfo> favorites,
        List<Telegram> news,
        MarketType marketType = MarketType.AShare,
        bool enable = true)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFavoriteService, StubFavoriteService>(
            marketType, (_, _) => new StubFavoriteService(favorites));
        services.AddKeyedSingleton<ITelegramService, StubTelegramService>(
            marketType, (_, _) => new StubTelegramService(news));
        using var provider = services.BuildServiceProvider();

        return new DailyBriefingService(
            marketType,
            provider,
            new StubChatClientFactory(),
            new StubUserSettingService(enable),
            NullLogger<DailyBriefingService>.Instance);
    }

    private static void ResetCounters()
    {
        _capturedRequests = [];
        _requestCount = 0;
    }

    /// <summary>删除测试产生的简报行（按内容前缀），不影响用户真实简报。</summary>
    private static void CleanupTestBriefings()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName,
            "market.db");
        if (!File.Exists(dbPath))
            return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM briefings WHERE content LIKE 'TESTBRIEF%'";
        cmd.ExecuteNonQuery();
    }

    [TestInitialize]
    public void Setup() => ResetCounters();

    [TestCleanup]
    public void Cleanup() => CleanupTestBriefings();

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_WithFavoritesAndNews_ShouldReturnBriefing()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%"), CreateFavorite("平安银行", "000001", "-0.8%")],
            [CreateNews("央行公开市场净投放"), CreateNews("隔夜美股收涨")]);

        var briefing = await service.GenerateAsync();

        Assert.IsNotNull(briefing);
        Assert.AreEqual(StubReplyContent, briefing.Content);
        Assert.AreEqual(2, briefing.FavoriteCount);
        Assert.AreEqual(StubModelId, briefing.ModelId);
        Assert.AreEqual(DateTime.Now.ToString("yyyy-MM-dd"), briefing.DateKey);
        Assert.AreEqual(1, _requestCount, "首次生成应恰好调用一次模型");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_SecondCallSameDay_ShouldReuseCacheWithoutCallingLlm()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%")],
            [CreateNews("央行公开市场净投放")]);

        var first = await service.GenerateAsync();
        var second = await service.GenerateAsync();

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Content, second.Content);
        Assert.AreEqual(1, _requestCount, "当天缓存命中不应再次调用模型");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_Force_ShouldRegenerateEvenWithCache()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%")],
            [CreateNews("央行公开市场净投放")]);

        await service.GenerateAsync();
        var forced = await service.GenerateAsync(force: true);

        Assert.IsNotNull(forced);
        Assert.AreEqual(2, _requestCount, "强制刷新应再次调用模型");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_NoFavoritesAndNews_ShouldReturnNullWithoutCallingLlm()
    {
        var service = CreateService([], []);

        var briefing = await service.GenerateAsync();

        Assert.IsNull(briefing, "自选与要闻均为空时跳过生成");
        Assert.AreEqual(0, _requestCount, "无输入不应调用模型");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_ShouldBoundFavoritesAndNewsWithinBudget()
    {
        // 自选给超额数量验证上限裁剪；要闻需超过 MaxNewsItems(40) 才能验证截断。
        var favorites = Enumerable.Range(1, 12)
            .Select(i => CreateFavorite($"股票{i}", $"60000{i}", $"{i % 2}.0%"))
            .ToList();
        var news = Enumerable.Range(1, DailyBriefingService.MaxNewsItems + 20)
            .Select(i => CreateNews($"快讯{i}", new string('x', 200)))
            .ToList();
        var service = CreateService(favorites, news);

        await service.GenerateAsync();

        Assert.AreEqual(1, _requestCount);
        var prompt = _capturedRequests[0].Text ?? string.Empty;
        var favoritesSection = prompt.Split("<favorites>")[1].Split("</favorites>")[0];
        var newsSection = prompt.Split("<news>")[1].Split("</news>")[0];
        Assert.AreEqual(DailyBriefingService.MaxFavorites, CountBulletLines(favoritesSection),
            "自选条数应钳制在预算内");
        Assert.AreEqual(DailyBriefingService.MaxNewsItems, CountBulletLines(newsSection),
            "要闻条数应钳制在预算内");
        Assert.Contains("x…", prompt, "超长要闻正文应被截断");
        Assert.Contains("禁止编造", prompt, "提示词应包含防幻觉约束");
    }

    /// <summary>
    /// 要闻标题也应被截断：标题是模型判断重要性的主要依据，超长标题需裁剪以控制 Token。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_ShouldTruncateOverlongNewsTitle()
    {
        var news = new List<Telegram>
        {
            new() { Time = "08:30", Title = new string('标', 60), Content = "正文" }
        };
        var service = CreateService([], news);

        await service.GenerateAsync(force: true);

        var prompt = _capturedRequests[0].Text ?? string.Empty;
        var newsSection = prompt.Split("<news>")[1].Split("</news>")[0];
        Assert.IsFalse(newsSection.Contains(new string('标', 60)), "超长标题应被截断");
        Assert.Contains("标…", newsSection, "截断后应保留省略标记");
    }

    /// <summary>
    /// 提示词应明确把重要性判断交给模型：既不要求固定条数，
    /// 也需保留禁止买卖建议等既有安全约束。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_ShouldLetModelJudgeImportance()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%")],
            [CreateNews("央行公开市场净投放")]);

        await service.GenerateAsync(force: true);

        var prompt = _capturedRequests[0].Text ?? string.Empty;
        Assert.Contains("自行判断重要性", prompt, "重要性判断应交由模型，而非代码硬过滤");
        Assert.DoesNotContain("隔夜", prompt, "已移除『隔夜』措辞——数据源不保证覆盖隔夜时段");
        Assert.Contains("禁止编造", prompt);
    }

    private static int CountBulletLines(string section)
        => section.Split('\n').Count(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal));

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetTodayAsync_WithoutCache_ShouldReturnNull()
    {
        var service = CreateService([], []);

        Assert.IsNull(await service.GetTodayAsync());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetTodayAsync_AfterGenerate_ShouldReturnCachedBriefing()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%")],
            [CreateNews("央行公开市场净投放")]);

        await service.GenerateAsync();
        var cached = await service.GetTodayAsync();

        Assert.IsNotNull(cached);
        Assert.AreEqual(StubReplyContent, cached.Content);
        Assert.AreEqual(MarketType.AShare, cached.MarketType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_FeatureDisabled_ShouldSkipEvenManualTrigger()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%")],
            [CreateNews("央行公开市场净投放")],
            enable: false);

        var cached = await service.GenerateAsync();
        var manual = await service.GenerateAsync(force: true);

        Assert.IsNull(cached);
        Assert.IsNull(manual, "总开关关闭时手动生成亦应被拦截");
        Assert.AreEqual(0, _requestCount, "功能关闭时不应产生任何模型调用与费用");
        Assert.IsFalse(service.IsEnabled);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_ShouldRecordTokenUsage()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%")],
            [CreateNews("央行公开市场净投放")]);

        var briefing = await service.GenerateAsync();

        Assert.IsNotNull(briefing);
        Assert.IsTrue(briefing.HasTokenUsage, "简报应记录 Token 用量，供成本可见");
        Assert.IsTrue(briefing.InputTokens > 0, "提示词非空，输入 Token 应大于 0");
        Assert.IsTrue(briefing.OutputTokens > 0, "模型有返回正文，输出 Token 应大于 0");
        Assert.IsTrue(briefing.Estimated, "桩客户端不返回 UsageContent，应标记为估算值");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateAsync_TokenUsage_ShouldSurviveCacheRoundTrip()
    {
        var service = CreateService(
            [CreateFavorite("贵州茅台", "600519", "+1.5%")],
            [CreateNews("央行公开市场净投放")]);

        var generated = await service.GenerateAsync();
        var cached = await service.GetTodayAsync();

        Assert.IsNotNull(generated);
        Assert.IsNotNull(cached);
        Assert.AreEqual(generated.InputTokens, cached.InputTokens, "Token 用量应随缓存持久化");
        Assert.AreEqual(generated.OutputTokens, cached.OutputTokens);
        Assert.AreEqual(generated.Estimated, cached.Estimated);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetUsageSummaryAsync_ShouldAggregateAcrossDays()
    {
        // 用 Crypto 市场做隔离：本类的生成类测试都落在 AShare 市场并写入"今天"，
        // 若共用市场，本用例的精确条数断言会被其他用例污染而偶发失败。
        var service = CreateService(
            [CreateFavorite("BTC", "BTCUSDT", "+1.5%")],
            [CreateNews("比特币放量上涨")],
            MarketType.Crypto);

        // 日期必须相对"今天"计算：硬编码日期会随时间推移滑出 30 天窗口而使测试失效
        var today = DateTime.Now;
        await InsertBriefingRowAsync(MarketType.Crypto, KeyOf(today.AddDays(-2)), 100, 200, estimated: false);
        await InsertBriefingRowAsync(MarketType.Crypto, KeyOf(today.AddDays(-1)), 300, 400, estimated: true);

        // 窗口外（400 天前）：不应被计入
        await InsertBriefingRowAsync(MarketType.Crypto, KeyOf(today.AddDays(-400)), 999_999, 999_999, estimated: false);

        var summary = await service.GetUsageSummaryAsync(days: 30);

        Assert.IsTrue(summary.HasValue);
        Assert.AreEqual(2, summary.DayCount, "窗口外的老数据不应计入");
        Assert.AreEqual(400L, summary.InputTokens);
        Assert.AreEqual(600L, summary.OutputTokens);
        Assert.AreEqual(1000L, summary.TotalTokens);
        Assert.IsTrue(summary.Estimated, "任一行为估算值则汇总标记为含估算");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetUsageSummaryAsync_ShouldBeScopedPerMarket()
    {
        // 双市场共用同一张表：汇总必须按 market_type 隔离，否则 A 股用户会看到币圈的消耗。
        // 断言用哨兵值而非"条数为 0"——本类其他用例会写入 A 股当天记录，
        // 断言 A 股为空会与那些用例互相干扰。
        var cryptoService = CreateService(
            [CreateFavorite("BTC", "BTCUSDT", "+1.0%")],
            [CreateNews("比特币上涨")],
            MarketType.Crypto);
        var aShareService = CreateService([], [], MarketType.AShare);

        const long SentinelInput = 777_777;
        await InsertBriefingRowAsync(MarketType.Crypto, KeyOf(DateTime.Now), SentinelInput, 888_888, estimated: false);

        var cryptoSummary = await cryptoService.GetUsageSummaryAsync(days: 30);
        var aShareSummary = await aShareService.GetUsageSummaryAsync(days: 30);

        Assert.IsTrue(cryptoSummary.HasValue);
        Assert.IsTrue(cryptoSummary.InputTokens >= SentinelInput, "Crypto 汇总应包含自身消耗");
        Assert.AreNotEqual(SentinelInput, aShareSummary.InputTokens, "A 股汇总不应混入 Crypto 的消耗");
    }

    private static string KeyOf(DateTime date) => date.ToString("yyyy-MM-dd");

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetUsageSummaryAsync_NoRecords_ShouldReturnEmpty()
    {
        var service = CreateService([], []);

        var summary = await service.GetUsageSummaryAsync(days: 30);

        Assert.IsFalse(summary.HasValue, "无历史记录时不应显示摘要");
        Assert.AreEqual(0L, summary.TotalTokens);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetUsageSummaryAsync_InvalidDays_ShouldClampInsteadOfThrow()
    {
        var service = CreateService([], []);

        // 0 / 负数 / 超大值都应被钳制为合法窗口，而不是抛异常或全表扫描
        await service.GetUsageSummaryAsync(days: 0);
        await service.GetUsageSummaryAsync(days: -5);
        var huge = await service.GetUsageSummaryAsync(days: 100_000);

        Assert.IsFalse(huge.HasValue);
    }

    /// <summary>
    /// 直连 market.db 插入测试用简报行（按内容前缀标识，测试清理阶段删除）。
    /// </summary>
    private static async Task InsertBriefingRowAsync(
        MarketType marketType,
        string dateKey,
        long input,
        long output,
        bool estimated)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName,
            "market.db");
        if (!File.Exists(dbPath))
            return;

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO briefings
                (market_type, date_key, content, generated_at, favorite_count, model_id,
                 input_tokens, output_tokens, token_estimated)
            VALUES (@market, @dateKey, @content, @generatedAt, 0, @modelId,
                    @input, @output, @estimated)
            """;
        cmd.Parameters.AddWithValue("@market", (int)marketType);
        cmd.Parameters.AddWithValue("@dateKey", dateKey);
        cmd.Parameters.AddWithValue("@content", StubReplyContent);
        cmd.Parameters.AddWithValue("@generatedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@modelId", StubModelId);
        cmd.Parameters.AddWithValue("@input", input);
        cmd.Parameters.AddWithValue("@output", output);
        cmd.Parameters.AddWithValue("@estimated", estimated ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }
}
