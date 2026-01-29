using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Tools;

/// <summary>
/// ICryptoMetricsTools 接口测试（虚拟币市场数据）
/// </summary>
[TestClass]
public class CryptoMetricsToolsTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHttpClient();

        services.AddKeyedSingleton<ICryptoMetricsTools, CryptoMetricsTools>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    [TestMethod]
    public async Task GetVolumeDistributionAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetVolumeDistributionAsync("BTC");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result[0].Volume > 0);
    }

    [TestMethod]
    public void GetFunctions_ShouldReturnValidAIFunctions()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var functions = service.GetFunctions().ToList();

        Assert.IsNotNull(functions);
        Assert.AreEqual(6, functions.Count); // 6个函数：OHLCV、深度、成交、市场指标、交易量分布、波动性
    }

    [TestMethod]
    public async Task GetOHLCVAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetOHLCVAsync("BTCUSDT", interval: MarketInterval.OneDay, limit: 10);

        Assert.IsNotNull(result);
        Assert.AreEqual("BTCUSDT", result.Symbol);
        Assert.AreEqual("1d", result.Interval);
        Assert.IsTrue(result.Candles.Count > 0);
        Assert.IsTrue(result.Candles[0].Close > 0);
    }

    [TestMethod]
    public async Task GetOrderBookDepthAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetOrderBookDepthAsync("BTCUSDT", limit: 10);

        Assert.IsNotNull(result);
        Assert.AreEqual("BTCUSDT", result.Symbol);
        Assert.IsTrue(result.Bids.Count > 0);
        Assert.IsTrue(result.Asks.Count > 0);
        Assert.IsTrue(result.BestBidPrice > 0);
        Assert.IsTrue(result.BestAskPrice > 0);
        Assert.IsTrue(result.Spread > 0);
    }

    [TestMethod]
    public async Task GetRecentTradesAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetRecentTradesAsync("BTCUSDT", limit: 100);

        Assert.IsNotNull(result);
        Assert.AreEqual("BTCUSDT", result.Symbol);
        Assert.IsTrue(result.Trades.Count > 0);
        Assert.IsTrue(result.TotalVolume > 0);
        Assert.IsTrue(result.BuyerVolumePercent + result.SellerVolumePercent == 100);
    }

    [TestMethod]
    public async Task GetMarketMetricsAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetMarketMetricsAsync("BTC");

        Assert.IsNotNull(result);
        Assert.AreEqual("BTC", result.Symbol);
        Assert.IsTrue(result.CurrentPriceUsd > 0);
        Assert.IsTrue(result.MarketCapUsd > 0);
        Assert.IsTrue(result.CirculatingSupply > 0);
        Assert.IsNotNull(result.MarketCapRank);
    }

    [TestMethod]
    public async Task GetVolatilityMetricsAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetVolatilityMetricsAsync("BTCUSDT", days: 30);

        Assert.IsNotNull(result);
        Assert.AreEqual("BTCUSDT", result.Symbol);
        Assert.IsTrue(result.AnnualizedVolatility > 0);
        Assert.IsTrue(result.DailyVolatility > 0);
        Assert.IsTrue(result.PeriodDays == 30);
    }
}
