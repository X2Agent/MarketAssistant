using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.MultiMarket;

/// <summary>
/// IAssetCacheService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class AssetCacheServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 注册被测试的服务
        services.AddKeyedSingleton<IAssetCacheService, AShareAssetCacheService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetCacheService, CryptoAssetCacheService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        // 清理缓存
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IAssetCacheService>(MarketType.AShare);
        var cryptoService = _serviceProvider.GetRequiredKeyedService<IAssetCacheService>(MarketType.Crypto);

        aShareService.Clear();
        cryptoService.Clear();

        _serviceProvider.Dispose();
    }

    [TestMethod]
    public async Task CacheAssetInfo_AShare_ShouldStore()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetCacheService>(MarketType.AShare);
        var assetInfo = new AssetInfo
        {
            Code = "SH600519",
            Name = "贵州茅台",
            CurrentPrice = "1800.50"
        };

        // Act
        service.CacheAssetInfo("SH600519", assetInfo);
        var cached = await service.GetCachedAssetInfoAsync("SH600519");

        // Assert
        Assert.IsNotNull(cached);
        Assert.AreEqual("SH600519", cached.Code);
        Assert.AreEqual("1800.50", cached.CurrentPrice);
    }

    [TestMethod]
    public async Task GetCachedAssetInfoAsync_AShare_NotExist_ShouldReturnNull()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetCacheService>(MarketType.AShare);

        // Act
        var cached = await service.GetCachedAssetInfoAsync("NOT_EXIST");

        // Assert
        Assert.IsNull(cached);
    }

    [TestMethod]
    public async Task Clear_AShare_ShouldRemoveAllCache()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetCacheService>(MarketType.AShare);
        var assetInfo = new AssetInfo { Code = "SH600519", Name = "贵州茅台" };
        service.CacheAssetInfo("SH600519", assetInfo);

        // Act
        service.Clear();
        var cached = await service.GetCachedAssetInfoAsync("SH600519");

        // Assert
        Assert.IsNull(cached);
    }

    [TestMethod]
    public async Task CacheAssetInfo_Crypto_ShouldStore()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetCacheService>(MarketType.Crypto);
        var assetInfo = new AssetInfo
        {
            Code = "BTCUSDT",
            Name = "Bitcoin",
            CurrentPrice = "45000.00"
        };

        // Act
        service.CacheAssetInfo("BTCUSDT", assetInfo);
        var cached = await service.GetCachedAssetInfoAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(cached);
        Assert.AreEqual("BTCUSDT", cached.Code);
        Assert.AreEqual("45000.00", cached.CurrentPrice);
    }

    [TestMethod]
    public async Task AShareAndCrypto_ShouldHaveSeparateCache()
    {
        // Arrange
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IAssetCacheService>(MarketType.AShare);
        var cryptoService = _serviceProvider.GetRequiredKeyedService<IAssetCacheService>(MarketType.Crypto);

        var aShareAsset = new AssetInfo { Code = "SH600519", Name = "贵州茅台", CurrentPrice = "1800" };
        var cryptoAsset = new AssetInfo { Code = "BTCUSDT", Name = "Bitcoin", CurrentPrice = "45000" };

        // Act
        aShareService.CacheAssetInfo("SH600519", aShareAsset);
        cryptoService.CacheAssetInfo("BTCUSDT", cryptoAsset);

        var aShareCached = await aShareService.GetCachedAssetInfoAsync("SH600519");
        var cryptoCached = await cryptoService.GetCachedAssetInfoAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(aShareCached);
        Assert.IsNotNull(cryptoCached);
        Assert.AreEqual("SH600519", aShareCached.Code);
        Assert.AreEqual("BTCUSDT", cryptoCached.Code);

        // 验证隔离性
        var aShareNotExist = await aShareService.GetCachedAssetInfoAsync("BTCUSDT");
        var cryptoNotExist = await cryptoService.GetCachedAssetInfoAsync("SH600519");
        Assert.IsNull(aShareNotExist);
        Assert.IsNull(cryptoNotExist);
    }
}
