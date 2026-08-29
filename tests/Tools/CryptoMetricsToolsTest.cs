using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.Tools;

/// <summary>
/// ICryptoMetricsTools 接口测试（虚拟币市场数据）
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class CryptoMetricsToolsTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        // BinanceMarketDataService 重构后依赖 IMemoryCache
        services.AddMemoryCache();
        // 注册命名 HttpClient（含 BaseAddress 与弹性策略），与生产配置一致
        services.AddNamedMarketHttpClients();
        // 注册虚拟币指标工具依赖的数据服务（Binance 行情 + CoinGecko 市场指标）
        services.AddSingleton<BinanceMarketDataService>();
        services.AddSingleton<CoinGeckoApiService>();
        // CryptoMetricsTools 依赖 IKLineService（虚拟币 K线服务，基于币安行情）
        services.AddKeyedSingleton<IKLineService, CryptoKLineService>(MarketType.Crypto);

        // 通过 Mock 注入 UserSetting（CoinGeckoApiKey 可为空，使用免费 API）
        var userSetting = new UserSetting();
        var userSettingServiceMock = new Mock<IUserSettingService>();
        userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(userSetting);
        services.AddSingleton<IUserSettingService>(userSettingServiceMock.Object);

        services.AddKeyedSingleton<ICryptoMetricsTools, CryptoMetricsTools>(MarketType.Crypto);

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
    public async Task GetVolumeDistributionAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetVolumeDistributionAsync("BTC");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result[0].Volume > 0);
        // 加强验证：交易所名称、占比、交易对数量等字段
        Assert.IsFalse(string.IsNullOrEmpty(result[0].Exchange), $"交易所名称不应为空，实际: '{result[0].Exchange}'");
        Assert.IsTrue(result[0].Percentage > 0, $"占比应大于0，实际: {result[0].Percentage}");
        Assert.IsTrue(result[0].PairCount > 0, $"交易对数量应大于0，实际: {result[0].PairCount}");
        // 所有条目的占比之和应接近 100（允许浮点精度误差）
        var totalPercentage = result.Sum(x => x.Percentage);
        Assert.IsTrue(totalPercentage > 98 && totalPercentage < 102, $"占比总和应接近100，实际: {totalPercentage}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GetFunctions_ShouldReturnValidAIFunctions()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var functions = service.GetFunctions().ToList();

        Assert.IsNotNull(functions);
        Assert.AreEqual(6, functions.Count); // 6个函数：OHLCV、深度、成交、市场指标、交易量分布、波动性
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetOHLCVAsync_ShouldReturnValidData()
    {
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoMetricsTools>(MarketType.Crypto);

        var result = await service.GetOHLCVAsync("BTCUSDT", interval: MarketInterval.OneDay, limit: 10);

        Assert.IsNotNull(result);
        Assert.AreEqual("BTCUSDT", result.Symbol);
        Assert.AreEqual("1d", result.Interval);
        Assert.IsTrue(result.Candles.Count > 0);
        Assert.IsTrue(result.Candles[0].Close > 0);
        // 加强验证：OHLC 关系与成交量/时间戳
        Assert.IsTrue(result.Candles[0].Open > 0, $"开盘价应大于0，实际: {result.Candles[0].Open}");
        Assert.IsTrue(result.Candles[0].High >= result.Candles[0].Low, $"最高价({result.Candles[0].High})应大于等于最低价({result.Candles[0].Low})");
        Assert.IsTrue(result.Candles[0].High >= result.Candles[0].Open, $"最高价({result.Candles[0].High})应大于等于开盘价({result.Candles[0].Open})");
        Assert.IsTrue(result.Candles[0].High >= result.Candles[0].Close, $"最高价({result.Candles[0].High})应大于等于收盘价({result.Candles[0].Close})");
        Assert.IsTrue(result.Candles[0].Low <= result.Candles[0].Open, $"最低价({result.Candles[0].Low})应小于等于开盘价({result.Candles[0].Open})");
        Assert.IsTrue(result.Candles[0].Low <= result.Candles[0].Close, $"最低价({result.Candles[0].Low})应小于等于收盘价({result.Candles[0].Close})");
        Assert.IsTrue(result.Candles[0].Volume > 0, $"成交量应大于0，实际: {result.Candles[0].Volume}");
        Assert.IsTrue(result.Candles[0].OpenTime > 0, $"开盘时间戳应大于0，实际: {result.Candles[0].OpenTime}");
        Assert.IsTrue(result.Candles[0].CloseTime > result.Candles[0].OpenTime, $"收盘时间戳({result.Candles[0].CloseTime})应大于开盘时间戳({result.Candles[0].OpenTime})");
    }

    [TestMethod]
    [TestCategory("Integration")]
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
    [TestCategory("Integration")]
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
    [TestCategory("Integration")]
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
    [TestCategory("Integration")]
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
