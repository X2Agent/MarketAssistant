using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using TestMarketAssistant;

namespace MarketAssistant.Tests.Crypto;

/// <summary>
/// CryptoAssetInfoService 批量行情测试（真实币安 API 集成验证）
/// </summary>
[TestClass]
public class CryptoAssetInfoServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddTestMarketDataHttpClients();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton<CoinGeckoApiService>();
        services.AddSingleton<BinanceMarketDataService>();
        services.AddSingleton<ICryptoAliasRegistry, CryptoAliasRegistry>();

        // 注册被测试的服务
        services.AddKeyedSingleton<IAssetInfoService, CryptoAssetInfoService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
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
    public async Task GetAssetInfosAsync_ShouldReturnSameLengthInOrder()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);
        var favorites = new List<FavoriteAsset>
        {
            new() { Code = "BTCUSDT", Market = "Binance" },
            new() { Code = "ETHUSDT", Market = "Binance" },
            new() { Code = "SOLUSDT", Market = "Binance" }
        };

        // Act
        var results = await service.GetAssetInfosAsync(favorites);

        // Assert：与请求等长同序，价格与涨跌幅已填充；批量路径不拉取市值（收藏列表不展示）
        Assert.AreEqual(favorites.Count, results.Count);
        Assert.IsNotNull(results[0]);
        Assert.AreEqual("BTCUSDT", results[0]!.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(results[0]!.CurrentPrice));
        Assert.IsFalse(string.IsNullOrWhiteSpace(results[0]!.ChangePercentage));
        Assert.IsNotNull(results[1]);
        Assert.IsNotNull(results[2]);
        Assert.IsNull(results[0]!.MarketCap);
    }
}