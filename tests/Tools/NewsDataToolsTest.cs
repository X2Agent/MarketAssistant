using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Tools;

/// <summary>
/// INewsDataTools 接口真实场景验证测试（覆盖 A股 和 虚拟币 实现）
///
/// 真实性说明：
/// - A股 GetNewsAsync：调用东方财富搜索 API（search-api-web.eastmoney.com），公开免费、无需签名
/// - 虚拟币 GetNewsAsync：调用 CoinTelegraph RSS（https://cointelegraph.com/rss），免费、无需密钥
/// </summary>
[TestClass]
public class NewsDataToolsTest
{
    private ServiceProvider? _serviceProvider;

    public TestContext? TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<IModelProviderAdapterFactory, ModelProviderAdapterFactory>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddHttpClient();
        services.AddTestMarketDataHttpClients();
        services.AddLogging();

        // CryptoNewsTools 依赖 ICryptoAliasRegistry（基于 CoinGecko 币种别名）
        services.AddMemoryCache();
        services.AddSingleton<CoinGeckoApiService>();
        services.AddSingleton<ICryptoAliasRegistry, CryptoAliasRegistry>();

        // 注册被测试的服务
        services.AddKeyedSingleton<INewsDataTools, AShareNewsTools>(MarketType.AShare);
        services.AddKeyedSingleton<INewsDataTools, CryptoNewsTools>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    #region A股新闻数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetNewsAsync_AShare_ShouldReturnValidData()
    {
        // Arrange - 贵州茅台 SH600519，东方财富搜索 API（公开免费）
        var service = _serviceProvider!.GetRequiredKeyedService<INewsDataTools>(MarketType.AShare);

        // Act - 真实调用东方财富搜索 API
        var newsData = await service.GetNewsAsync("SH600519");

        // Assert - 真实验证：东方财富应返回贵州茅台相关新闻（非空 + 内容校验）
        Assert.IsNotNull(newsData, "新闻列表不应为空");
        Assert.IsTrue(newsData.Count > 0, "东方财富应返回至少 1 条新闻");
        var firstNews = newsData[0];
        Assert.IsFalse(string.IsNullOrEmpty(firstNews.Title), "新闻标题不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(firstNews.Link), "新闻链接不应为空");
        Assert.IsTrue(firstNews.Title.Length > 4, $"新闻标题应有实质内容（长度>4），实际: {firstNews.Title}");
        Assert.IsTrue(firstNews.Link.StartsWith("http"), $"新闻链接应为合法 URL，实际: {firstNews.Link}");
        TestContext?.WriteLine($"A股新闻数量: {newsData.Count}, 首条标题: {firstNews.Title}");
    }

    #endregion

    #region 虚拟币新闻数据测试

    /// <summary>
    /// 虚拟币新闻测试 - 真实场景验证（CoinTelegraph RSS）
    /// RSS 源免费可用，真实返回加密货币相关新闻
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetNewsAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange - BTC，CoinTelegraph RSS 免费源
        var service = _serviceProvider!.GetRequiredKeyedService<INewsDataTools>(MarketType.Crypto);

        // Act - 真实调用 CoinTelegraph RSS
        var newsData = await service.GetNewsAsync("btc");

        // Assert - 真实验证 RSS 返回的新闻数据（非空 + 内容校验）
        Assert.IsNotNull(newsData, "新闻列表不应为空");
        Assert.IsTrue(newsData.Count > 0, "RSS 应返回至少 1 条新闻");
        var firstNews = newsData[0];
        Assert.IsFalse(string.IsNullOrEmpty(firstNews.Title), "新闻标题不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(firstNews.Link), "新闻链接不应为空");
        Assert.IsTrue(firstNews.Link.StartsWith("http"), $"新闻链接应为合法 URL，实际: {firstNews.Link}");
        TestContext?.WriteLine($"虚拟币新闻数量: {newsData.Count}, 首条标题: {firstNews.Title}");
    }

    #endregion
}
