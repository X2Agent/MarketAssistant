using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton<BinanceMarketDataService>();

        // 注册 AssetInfoService（FavoriteService 的依赖）
        services.AddKeyedSingleton<IAssetInfoService, AShareAssetInfoService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);

        // 注册被测试的服务
        services.AddKeyedSingleton<IFavoriteService, AShareFavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<IFavoriteService, CryptoFavoriteService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        // 清理收藏
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        var cryptoService = _serviceProvider.GetRequiredKeyedService<IFavoriteService>(MarketType.Crypto);

        aShareService.ClearFavorites();
        cryptoService.ClearFavorites();

        await _serviceProvider.DisposeAsync();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void AddFavorite_AShare_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);

        // Act
        service.AddFavorite("SH600519", "");

        // Assert
        Assert.IsTrue(service.IsFavorite("SH600519", ""));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void RemoveFavorite_AShare_ShouldRemoveAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        service.AddFavorite("SH600519", "");

        // Act
        service.RemoveFavorite("SH600519", "");

        // Assert
        Assert.IsFalse(service.IsFavorite("SH600519", ""));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GetFavoritesCodes_AShare_ShouldReturnList()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        service.AddFavorite("SH600519", "");
        service.AddFavorite("SH600036", "");

        // Act
        var favorites = service.GetFavoritesCodes();

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
        service.AddFavorite("SH600519", "");

        // Act
        var favoritesWithData = await service.GetFavoritesWithLatestDataAsync();

        // Assert
        Assert.IsNotNull(favoritesWithData);
        Assert.IsTrue(favoritesWithData.Count > 0);
        Assert.IsTrue(favoritesWithData.Any(f => f.Code == "SH600519"));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void ClearFavorites_AShare_ShouldRemoveAll()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        service.AddFavorite("SH600519", "");
        service.AddFavorite("SH600036", "");

        // Act
        service.ClearFavorites();
        var favorites = service.GetFavoritesCodes();

        // Assert
        Assert.IsNotNull(favorites);
        Assert.AreEqual(0, favorites.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void AddFavorite_Crypto_ShouldStoreAsset()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.Crypto);

        // Act
        service.AddFavorite("BTCUSDT", "");

        // Assert
        Assert.IsTrue(service.IsFavorite("BTCUSDT", ""));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void AShareAndCrypto_ShouldHaveSeparateStorage()
    {
        // Arrange
        var aShareService = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);
        var cryptoService = _serviceProvider.GetRequiredKeyedService<IFavoriteService>(MarketType.Crypto);

        // Act
        aShareService.AddFavorite("SH600519", "");
        cryptoService.AddFavorite("BTCUSDT", "");

        var aShareFavorites = aShareService.GetFavoritesCodes();
        var cryptoFavorites = cryptoService.GetFavoritesCodes();

        // Assert
        Assert.AreEqual(1, aShareFavorites.Count);
        Assert.AreEqual(1, cryptoFavorites.Count);
        Assert.AreEqual("SH600519", aShareFavorites[0].Code);
        Assert.AreEqual("BTCUSDT", cryptoFavorites[0].Code);
    }
}
