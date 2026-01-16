using MarketAssistant.Applications.Assets;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.MultiMarket;

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
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<MarketContext>();
        services.AddSingleton<PlaywrightService>();
        services.AddLogging();

        services.AddMemoryCache();

        // 注册被测试的服务
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
        _marketContext = _serviceProvider.GetRequiredService<MarketContext>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    [TestMethod]
    public async Task SearchAsync_AShare_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.AShare);

        // Act
        var results = await service.SearchAsync("贵州茅台");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0);
        Assert.IsTrue(results.Any(r => r.Name.Contains("茅台")));
    }

    [TestMethod]
    public async Task GetAssetInfoAsync_AShare_ShouldReturnAssetDetails()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.AShare);

        // Act
        var assetInfo = await service.GetAssetInfoAsync("SH600519");

        // Assert
        Assert.IsNotNull(assetInfo);
        Assert.AreEqual("SH600519", assetInfo.Code);
        Assert.IsTrue(assetInfo.Name.Contains("茅台"));
    }

    [TestMethod]
    public async Task GetHotAssetsAsync_AShare_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.AShare);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0);
        Assert.IsTrue(hotAssets.All(h => !string.IsNullOrEmpty(h.Code)));
    }

    [TestMethod]
    public async Task SearchAsync_Crypto_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);

        // Act
        var results = await service.SearchAsync("BTC");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0);
        Assert.IsTrue(results.Any(r => r.Code.Contains("BTC")));
    }

    [TestMethod]
    public async Task GetAssetInfoAsync_Crypto_ShouldReturnAssetDetails()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);

        // Act
        var assetInfo = await service.GetAssetInfoAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(assetInfo);
        Assert.IsTrue(assetInfo.Code.Contains("BTC"));
        Assert.IsFalse(string.IsNullOrEmpty(assetInfo.CurrentPrice));
    }

    [TestMethod]
    public async Task GetHotAssetsAsync_Crypto_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0);
        Assert.IsTrue(hotAssets.All(h => !string.IsNullOrEmpty(h.Code)));
    }
}
