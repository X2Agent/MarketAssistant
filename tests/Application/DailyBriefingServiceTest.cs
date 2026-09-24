using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Briefing;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
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

    private static List<ChatMessage> _capturedRequests = new();
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

    private static DailyBriefingService CreateService(
        List<AssetInfo> favorites,
        List<Telegram> news,
        MarketType marketType = MarketType.AShare)
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
            NullLogger<DailyBriefingService>.Instance);
    }

    private static void ResetCounters()
    {
        _capturedRequests = new List<ChatMessage>();
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
        var favorites = Enumerable.Range(1, 12)
            .Select(i => CreateFavorite($"股票{i}", $"60000{i}", $"{i % 2}.0%"))
            .ToList();
        var news = Enumerable.Range(1, 12)
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
        Assert.IsTrue(prompt.Contains("x…"), "超长要闻正文应被截断");
        StringAssert.Contains(prompt, "禁止编造", "提示词应包含防幻觉约束");
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
}
