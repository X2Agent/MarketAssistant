using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.History;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Application;

/// <summary>
/// IAssetHistoryService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
[DoNotParallelize]
public class AssetHistoryServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        Preferences.Default.Clear("RecentAssets_AShare");
        Preferences.Default.Clear("RecentAssets_Crypto");

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKeyedSingleton<IAssetHistoryService, AShareHistoryService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetHistoryService, CryptoHistoryService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        var cryptoService = _serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);

        aShareService.ClearHistory();
        cryptoService.ClearHistory();

        await _serviceProvider.DisposeAsync();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AddHistory_AShare_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        var asset = new AssetItem { Code = "SH600519", Name = "贵州茅台" };

        // Act
        service.AddHistory(asset);
        var history = service.GetHistory();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("SH600519", history[0].Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetHistory_AShare_ShouldReturnRecentAssets()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        service.AddHistory(new AssetItem { Code = "SH600519", Name = "贵州茅台" });
        service.AddHistory(new AssetItem { Code = "SH600036", Name = "招商银行" });

        // Act
        var history = service.GetHistory();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("SH600036", history[0].Code); // 最新的在前面
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ClearHistory_AShare_ShouldRemoveAllRecords()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        service.AddHistory(new AssetItem { Code = "SH600519", Name = "贵州茅台" });

        // Act
        service.ClearHistory();
        var history = service.GetHistory();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(0, history.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AddHistory_Crypto_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);
        var asset = new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" };

        // Act
        service.AddHistory(asset);
        var history = service.GetHistory();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("BTCUSDT", history[0].Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetHistory_Crypto_ShouldReturnRecentAssets()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);
        service.AddHistory(new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" });
        service.AddHistory(new AssetItem { Code = "ETHUSDT", Name = "Ethereum" });

        // Act
        var history = service.GetHistory();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("ETHUSDT", history[0].Code); // 最新的在前面
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AShareAndCrypto_ShouldHaveSeparateStorage()
    {
        // Arrange
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        var cryptoService = _serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);

        // Act
        aShareService.AddHistory(new AssetItem { Code = "SH600519", Name = "贵州茅台" });
        cryptoService.AddHistory(new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" });

        var aShareHistory = aShareService.GetHistory();
        var cryptoHistory = cryptoService.GetHistory();

        // Assert
        Assert.AreEqual(1, aShareHistory.Count);
        Assert.AreEqual(1, cryptoHistory.Count);
        Assert.AreEqual("SH600519", aShareHistory[0].Code);
        Assert.AreEqual("BTCUSDT", cryptoHistory[0].Code);
    }
}
