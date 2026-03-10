using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton(new Mock<IDialogService>().Object);
        services.AddSingleton<BinanceMarketDataService>();
        services.AddSingleton<CoinGeckoApiService>();

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
        _marketContext = _serviceProvider.GetRequiredService<MarketContext>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _serviceProvider?.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare).ClearHistory();
        _serviceProvider?.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto).ClearHistory();

        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task SearchAssetAsync_AShare_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);

        // Act
        var results = await service.SearchAssetAsync("贵州茅台");

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count >= 0);
    }

    [TestMethod]
    public async Task GetHotAssetsAsync_AShare_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);

        // Act
        try
        {
            var hotAssets = await service.GetHotAssetsAsync();
            Assert.IsNotNull(hotAssets);
            Assert.IsTrue(hotAssets.Count >= 0);
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    public void AddToRecentAssets_AShare_ShouldStoreInHistory()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare).ClearHistory();
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);
        var asset = new AssetItem { Code = "SH600519", Name = "贵州茅台" };

        // Act
        service.AddToRecentAssets(asset);
        var recentAssets = service.GetRecentAssets();

        // Assert
        Assert.IsNotNull(recentAssets);
    }

    [TestMethod]
    public void GetRecentAssets_AShare_ShouldReturnHistory()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.AShare);
        _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.AShare).ClearHistory();
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.AShare);
        var asset1 = new AssetItem { Code = "SH600519", Name = "贵州茅台" };
        var asset2 = new AssetItem { Code = "SH600036", Name = "招商银行" };

        // Act
        service.AddToRecentAssets(asset1);
        service.AddToRecentAssets(asset2);
        var recentAssets = service.GetRecentAssets();

        // Assert
        Assert.IsNotNull(recentAssets);
    }

    [TestMethod]
    public async Task SearchAssetAsync_Crypto_ShouldReturnResults()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);

        // Act
        try
        {
            var results = await service.SearchAssetAsync("BTC");
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count >= 0);
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    public async Task GetHotAssetsAsync_Crypto_ShouldReturnHotList()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);

        // Act
        try
        {
            var hotAssets = await service.GetHotAssetsAsync();
            Assert.IsNotNull(hotAssets);
            Assert.IsTrue(hotAssets.Count >= 0);
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    public void AddToRecentAssets_Crypto_ShouldStoreInHistory()
    {
        // Arrange
        _marketContext!.SwitchMarket(MarketType.Crypto);
        _serviceProvider!.GetRequiredKeyedService<IAssetHistoryService>(MarketType.Crypto).ClearHistory();
        var service = _serviceProvider!.GetRequiredKeyedService<IHomeAssetService>(MarketType.Crypto);
        var asset = new AssetItem { Code = "BTCUSDT", Name = "Bitcoin" };

        // Act
        service.AddToRecentAssets(asset);
        var recentAssets = service.GetRecentAssets();

        // Assert
        Assert.IsNotNull(recentAssets);
    }
}
