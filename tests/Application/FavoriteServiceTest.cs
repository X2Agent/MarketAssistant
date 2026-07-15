using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Application;

/// <summary>
/// IFavoriteService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class FavoriteServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddHttpClient();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<MarketContext>();
        services.AddSingleton<BinanceMarketDataService>();

        // 注册 AssetInfoService（FavoriteService 的依赖）
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);

        // 注册被测试的服务
        services.AddKeyedSingleton<IFavoriteService, FavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<IFavoriteService, FavoriteService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        // 清理收藏
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        var cryptoService = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.Crypto);

        await aShareService.ClearFavoritesAsync();
        await cryptoService.ClearFavoritesAsync();

        await _serviceProvider!.DisposeAsync();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task AddFavorite_AShare_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);

        // Act
        await service.AddFavoriteAsync("SH600519", "");

        // Assert
        Assert.IsTrue(await service.IsFavoriteAsync("SH600519", ""));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task RemoveFavorite_AShare_ShouldRemoveAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        await service.AddFavoriteAsync("SH600519", "");

        // Act
        await service.RemoveFavoriteAsync("SH600519", "");

        // Assert
        Assert.IsFalse(await service.IsFavoriteAsync("SH600519", ""));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetFavoritesCodes_AShare_ShouldReturnList()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        await service.AddFavoriteAsync("SH600519", "");
        await service.AddFavoriteAsync("SH600036", "");

        // Act
        var favorites = await service.GetFavoritesCodesAsync();

        // Assert
        Assert.IsNotNull(favorites);
        Assert.AreEqual(2, favorites.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetFavoritesWithLatestDataAsync_AShare_ShouldReturnAssetInfo()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        await service.AddFavoriteAsync("SH600519", "");

        // Act
        var favoritesWithData = await service.GetFavoritesWithLatestDataAsync();

        // Assert
        Assert.IsNotNull(favoritesWithData);
        Assert.IsTrue(favoritesWithData.Count > 0);
        Assert.IsTrue(favoritesWithData.Any(f => f.Code == "SH600519"));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ClearFavorites_AShare_ShouldRemoveAll()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        await service.AddFavoriteAsync("SH600519", "");
        await service.AddFavoriteAsync("SH600036", "");

        // Act
        await service.ClearFavoritesAsync();
        var favorites = await service.GetFavoritesCodesAsync();

        // Assert
        Assert.IsNotNull(favorites);
        Assert.AreEqual(0, favorites.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task AddFavorite_Crypto_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.Crypto);

        // Act
        await service.AddFavoriteAsync("BTCUSDT", "");

        // Assert
        Assert.IsTrue(await service.IsFavoriteAsync("BTCUSDT", ""));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task AShareAndCrypto_ShouldHaveSeparateStorage()
    {
        // Arrange
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        var cryptoService = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.Crypto);

        // Act
        await aShareService.AddFavoriteAsync("SH600519", "");
        await cryptoService.AddFavoriteAsync("BTCUSDT", "");

        var aShareFavorites = await aShareService.GetFavoritesCodesAsync();
        var cryptoFavorites = await cryptoService.GetFavoritesCodesAsync();

        // Assert
        Assert.AreEqual(1, aShareFavorites.Count);
        Assert.AreEqual(1, cryptoFavorites.Count);
        Assert.AreEqual("SH600519", aShareFavorites[0].Code);
        Assert.AreEqual("BTCUSDT", cryptoFavorites[0].Code);
    }
}
