using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.MultiMarket;

/// <summary>
/// IHomeAssetService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class HomeAssetServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<MarketContext>();
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton(new Mock<IDialogService>().Object);
        services.AddLogging();

        // 注册依赖的服务
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);
        services.AddKeyedSingleton<IAssetHistoryService, AShareHistoryService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetHistoryService, CryptoHistoryService>(MarketType.Crypto);
        services.AddKeyedSingleton<IFavoriteService, AShareFavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<IFavoriteService, CryptoFavoriteService>(MarketType.Crypto);

        // 注册被测试的服务
        services.AddKeyedSingleton<IHomeAssetService, AShareHomeService>(MarketType.AShare);
        services.AddKeyedSingleton<IHomeAssetService, CryptoHomeService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    [TestMethod]
    public async Task SearchAssetAsync_AShare_ShouldReturnResults()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);

        // Act
        var results = await service.SearchAssetAsync("贵州茅台");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0);
        Assert.IsTrue(results.Any(r => r.Name.Contains("茅台")));
    }

    [TestMethod]
    public async Task GetHotAssetsAsync_AShare_ShouldReturnHotList()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0);
    }

    [TestMethod]
    public void AddToRecentAssets_AShare_ShouldStoreInHistory()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);
        var asset = new AssetItem { Code = "SH600519", Name = "贵州茅台" };

        // Act
        service.AddToRecentAssets(asset);
        var recentAssets = service.GetRecentAssets();

        // Assert
        Assert.IsNotNull(recentAssets);
        Assert.IsTrue(recentAssets.Any(a => a.Code == "SH600519"));
    }

    [TestMethod]
    public void GetRecentAssets_AShare_ShouldReturnHistory()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);
        var asset1 = new AssetItem { Code = "SH600519", Name = "贵州茅台" };
        var asset2 = new AssetItem { Code = "SH600036", Name = "招商银行" };

        // Act
        service.AddToRecentAssets(asset1);
        service.AddToRecentAssets(asset2);
        var recentAssets = service.GetRecentAssets();

        // Assert
        Assert.IsNotNull(recentAssets);
        Assert.AreEqual(2, recentAssets.Count);
    }

    [TestMethod]
    public async Task SearchAssetAsync_Crypto_ShouldReturnResults()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);

        // Act
        var results = await service.SearchAssetAsync("BTC");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0);
        Assert.IsTrue(results.Any(r => r.Code.Contains("BTC")));
    }

    [TestMethod]
    public async Task GetHotAssetsAsync_Crypto_ShouldReturnHotList()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);

        // Act
        var hotAssets = await service.GetHotAssetsAsync();

        // Assert
        Assert.IsNotNull(hotAssets);
        Assert.IsTrue(hotAssets.Count > 0);
    }

    [TestMethod]
    public void AddToRecentAssets_Crypto_ShouldStoreInHistory()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);
        var asset = new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" };

        // Act
        service.AddToRecentAssets(asset);
        var recentAssets = service.GetRecentAssets();

        // Assert
        Assert.IsNotNull(recentAssets);
        Assert.IsTrue(recentAssets.Any(a => a.Code == "BTCUSDT"));
    }
}
