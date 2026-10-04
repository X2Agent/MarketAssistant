using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Application;

/// <summary>
/// IFavoriteService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
/// <remarks>
/// 该类所有方法共用同一份 SQLite 收藏库文件，方法级并行会相互清空数据，
/// 因此必须整体串行执行。
/// </remarks>
[TestClass]
[DoNotParallelize]
public class FavoriteServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddAShareDataProviders();

        // 注册依赖服务
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddTestMarketDataHttpClients();
        services.AddSingleton<MarketContext>();
        services.AddSingleton<CoinGeckoApiService>();
        services.AddSingleton<ICryptoAliasRegistry, CryptoAliasRegistry>();
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
        Assert.HasCount(2, favorites);
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
        Assert.IsNotEmpty(favoritesWithData);
        // 存储与返回均为归一化后的代码（A股剥离 SH/SZ 前缀）
        Assert.Contains(f => f.Code == "600519", favoritesWithData);
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
        Assert.IsEmpty(favorites);
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
        Assert.HasCount(1, aShareFavorites);
        Assert.HasCount(1, cryptoFavorites);
        Assert.AreEqual("600519", aShareFavorites[0].Code);
        Assert.AreEqual("BTCUSDT", cryptoFavorites[0].Code);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task FavoriteCode_ShouldNormalizeAcrossWriteAndQueryPaths()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);

        // 写入端：大小写与交易所前缀均被归一化
        await service.AddFavoriteAsync("sh600519", "SH");
        var favorites = await service.GetFavoritesCodesAsync();
        Assert.HasCount(1, favorites);
        Assert.AreEqual("600519", favorites[0].Code);

        // 查询端：无论传入原始代码还是归一化代码，都必须判定为已收藏
        Assert.IsTrue(await service.IsFavoriteAsync("600519", "SH"));
        Assert.IsTrue(await service.IsFavoriteAsync("SH600519", "SH"));
        Assert.IsTrue(await service.IsFavoriteAsync("sh600519", "SH"));

        // 重复写入归一化后的同一资产不应产生第二条记录
        await service.AddFavoriteAsync("SH600519", "SH");
        Assert.HasCount(1, await service.GetFavoritesCodesAsync());

        // 移除端同样归一化，且原始代码调用也能命中
        await service.RemoveFavoriteAsync("sh600519", "SH");
        Assert.IsFalse(await service.IsFavoriteAsync("600519", "SH"));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task IsFavorite_BlankCode_ShouldReturnFalseWithoutThrowing()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<IFavoriteService>(MarketType.AShare);

        Assert.IsFalse(await service.IsFavoriteAsync("", "SH"));
        Assert.IsFalse(await service.IsFavoriteAsync("   ", "SH"));
    }
}
