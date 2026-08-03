using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.History;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKeyedSingleton<IAssetHistoryService, AssetHistoryService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetHistoryService, AssetHistoryService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        var cryptoService = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);

        await aShareService.ClearHistoryAsync();
        await cryptoService.ClearHistoryAsync();

        await _serviceProvider!.DisposeAsync();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AddHistory_AShare_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        var asset = new AssetItem { Code = "SH600519", Name = "贵州茅台" };

        // Act
        await service.AddHistoryAsync(asset);
        var history = await service.GetHistoryAsync();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("SH600519", history[0].Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetHistory_AShare_ShouldReturnRecentAssets()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        await service.AddHistoryAsync(new AssetItem { Code = "SH600519", Name = "贵州茅台" });
        await service.AddHistoryAsync(new AssetItem { Code = "SH600036", Name = "招商银行" });

        // Act
        var history = await service.GetHistoryAsync();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("SH600036", history[0].Code); // 最新的在前面
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ClearHistory_AShare_ShouldRemoveAllRecords()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        await service.AddHistoryAsync(new AssetItem { Code = "SH600519", Name = "贵州茅台" });

        // Act
        await service.ClearHistoryAsync();
        var history = await service.GetHistoryAsync();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(0, history.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AddHistory_Crypto_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);
        var asset = new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" };

        // Act
        await service.AddHistoryAsync(asset);
        var history = await service.GetHistoryAsync();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("BTCUSDT", history[0].Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetHistory_Crypto_ShouldReturnRecentAssets()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);
        await service.AddHistoryAsync(new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" });
        await service.AddHistoryAsync(new AssetItem { Code = "ETHUSDT", Name = "Ethereum" });

        // Act
        var history = await service.GetHistoryAsync();

        // Assert
        Assert.IsNotNull(history);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("ETHUSDT", history[0].Code); // 最新的在前面
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AShareAndCrypto_ShouldHaveSeparateStorage()
    {
        // Arrange
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare);
        var cryptoService = _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto);

        // Act
        await aShareService.AddHistoryAsync(new AssetItem { Code = "SH600519", Name = "贵州茅台" });
        await cryptoService.AddHistoryAsync(new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" });

        var aShareHistory = await aShareService.GetHistoryAsync();
        var cryptoHistory = await cryptoService.GetHistoryAsync();

        // Assert
        Assert.AreEqual(1, aShareHistory.Count);
        Assert.AreEqual(1, cryptoHistory.Count);
        Assert.AreEqual("SH600519", aShareHistory[0].Code);
        Assert.AreEqual("BTCUSDT", cryptoHistory[0].Code);
    }
}
