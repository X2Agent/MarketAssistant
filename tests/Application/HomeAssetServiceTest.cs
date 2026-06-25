using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// IHomeAssetService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class HomeAssetServiceTest
{
    private ServiceProvider? _serviceProvider;
    private MarketContext? _marketContext;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddHttpClient();
        services.AddTestMarketDataHttpClients();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<MarketContext>();
        services.AddSingleton(new Mock<IDialogService>().Object);
        services.AddSingleton<BinanceMarketDataService>();
        services.AddSingleton<CoinGeckoApiService>();
        services.AddSingleton<ICryptoAliasRegistry, CryptoAliasRegistry>();

        // 注册依赖的服务
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetHistoryService, AssetHistoryService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetHistoryService, AssetHistoryService>(MarketType.Crypto);
        services.AddKeyedSingleton<IFavoriteService, FavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<IFavoriteService, FavoriteService>(MarketType.Crypto);

        // 注册被测试的服务
        services.AddKeyedSingleton<IHomeAssetService, HomeAssetService>(MarketType.AShare);
        services.AddKeyedSingleton<IHomeAssetService, HomeAssetService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
        _marketContext = _serviceProvider.GetRequiredService<MarketContext>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _serviceProvider?.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare).ClearHistoryAsync();
        await _serviceProvider?.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto).ClearHistoryAsync();

        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task SearchAssetAsync_AShare_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);

        // Act
        var results = await service.SearchAssetAsync("贵州茅台");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0, "搜索'贵州茅台'应返回至少一条结果");
        var first = results[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.Code), "返回结果应包含有效的股票代码");
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.Name), "返回结果应包含有效的股票名称");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetHotAssetsAsync_AShare_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0, "热门资产列表不应为空");
        Assert.IsTrue(hotAssets.All(h => !string.IsNullOrWhiteSpace(h.Code)), "所有热门资产应包含有效代码");
        Assert.IsTrue(hotAssets.All(h => !string.IsNullOrWhiteSpace(h.Name)), "所有热门资产应包含有效名称");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task AddToRecentAssets_AShare_ShouldStoreInHistory()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        await _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare).ClearHistoryAsync();
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);
        var asset = new AssetItem { Code = "SH600519", Name = "贵州茅台" };

        // Act
        await service.AddToRecentAssetsAsync(asset);
        var recentAssets = await service.GetRecentAssetsAsync();

        // Assert
        Assert.IsNotNull(recentAssets);
        Assert.IsTrue(recentAssets.Count > 0, "添加后最近资产列表不应为空");
        Assert.IsTrue(recentAssets.Any(a => a.Code == "SH600519"), "最近资产应包含刚添加的贵州茅台");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetRecentAssets_AShare_ShouldReturnHistory()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        await _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare).ClearHistoryAsync();
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);
        var asset1 = new AssetItem { Code = "SH600519", Name = "贵州茅台" };
        var asset2 = new AssetItem { Code = "SH600036", Name = "招商银行" };

        // Act
        await service.AddToRecentAssetsAsync(asset1);
        await service.AddToRecentAssetsAsync(asset2);
        var recentAssets = await service.GetRecentAssetsAsync();

        // Assert
        Assert.IsNotNull(recentAssets);
        Assert.IsTrue(recentAssets.Count >= 2, "添加两个资产后最近列表应至少包含两条记录");
        Assert.IsTrue(recentAssets.Any(a => a.Code == "SH600519"), "应包含贵州茅台");
        Assert.IsTrue(recentAssets.Any(a => a.Code == "SH600036"), "应包含招商银行");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task SearchAssetAsync_Crypto_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);

        // Act
        var results = await service.SearchAssetAsync("BTC");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0, "搜索'BTC'应返回至少一条结果");
        Assert.IsTrue(results.All(r => !string.IsNullOrWhiteSpace(r.Code)), "所有结果应包含有效代码");
        Assert.IsTrue(results.Any(r => r.Code.Contains("BTC")), "结果中应包含 BTC 相关资产");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetHotAssetsAsync_Crypto_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0, "虚拟币热门资产列表不应为空");
        Assert.IsTrue(hotAssets.All(h => !string.IsNullOrWhiteSpace(h.Code)), "所有热门资产应包含有效代码");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task AddToRecentAssets_Crypto_ShouldStoreInHistory()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        await _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto).ClearHistoryAsync();
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);
        var asset = new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" };

        // Act
        await service.AddToRecentAssetsAsync(asset);
        var recentAssets = await service.GetRecentAssetsAsync();

        // Assert
        Assert.IsNotNull(recentAssets);
        Assert.IsTrue(recentAssets.Count > 0, "添加后最近资产列表不应为空");
        Assert.IsTrue(recentAssets.Any(a => a.Code == "BTCUSDT"), "最近资产应包含刚添加的 BTCUSDT");
    }
}
