using MarketAssistant.Applications.Assets;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Application;

/// <summary>
/// IAssetInfoService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class AssetInfoServiceTest
{
    private ServiceProvider? _serviceProvider;
    private MarketContext? _marketContext;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddHttpClient();
        services.AddAShareDataProviders();
        services.AddTestMarketDataHttpClients();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<MarketContext>();
        services.AddSingleton<BinanceMarketDataService>();
        services.AddSingleton<CoinGeckoApiService>();

        // 注册被测试的服务
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
        _marketContext = _serviceProvider.GetRequiredService<MarketContext>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task SearchAsync_AShare_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.AShare);

        // Act
        var results = await service.SearchAsync("贵州茅台");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0, "搜索'贵州茅台'应返回至少一条结果");
        Assert.IsTrue(results.All(r => !string.IsNullOrWhiteSpace(r.Code)), "所有结果应包含有效代码");
        Assert.IsTrue(results.Any(r => r.Name.Contains("茅台")), "结果中应包含茅台相关股票");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetAssetInfoAsync_AShare_ShouldReturnAssetDetails()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.AShare);

        // Act
        var assetInfo = await service.GetAssetInfoAsync("SH600519");

        // Assert
        Assert.IsNotNull(assetInfo);
        Assert.AreEqual(MarketType.AShare, assetInfo.MarketType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(assetInfo.Code));
        Assert.IsFalse(string.IsNullOrWhiteSpace(assetInfo.Name));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetHotAssetsAsync_AShare_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.AShare);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0, "A股热门资产列表不应为空");
        Assert.IsTrue(hotAssets.All(h => !string.IsNullOrEmpty(h.Code)), "所有热门资产应包含有效代码");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task SearchAsync_Crypto_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);

        // Act
        var results = await service.SearchAsync("BTC");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0, "搜索'BTC'应返回至少一条结果");
        Assert.IsTrue(results.All(r => !string.IsNullOrWhiteSpace(r.Code)), "所有结果应包含有效代码");
        Assert.IsTrue(results.Any(r => r.Code.Contains("BTC")), "结果中应包含 BTC 相关资产");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetAssetInfoAsync_Crypto_ShouldReturnAssetDetails()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);

        // Act
        var assetInfo = await service.GetAssetInfoAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(assetInfo);
        Assert.IsTrue(assetInfo.Code.Contains("BTC"), "返回的代码应包含 BTC");
        Assert.IsFalse(string.IsNullOrEmpty(assetInfo.CurrentPrice), "应返回当前价格");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetHotAssetsAsync_Crypto_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0, "虚拟币热门资产列表不应为空");
        Assert.IsTrue(hotAssets.All(h => !string.IsNullOrEmpty(h.Code)), "所有热门资产应包含有效代码");
    }
}
