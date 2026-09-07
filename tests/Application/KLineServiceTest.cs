using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// IKLineService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class KLineServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册 A 股数据提供者（ZhiTuMarketClient，AShareKLineService 构造依赖）
        services.AddAShareDataProviders();
        // BinanceMarketDataService 重构后依赖 IMemoryCache
        services.AddMemoryCache();

        // 注册依赖服务（智兔令牌从环境变量读取，不在代码中硬编码）
        var mockUserSettingService = new Mock<IUserSettingService>();
        mockUserSettingService.Setup(s => s.CurrentSetting)
            .Returns(new UserSetting
            {
                ZhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? ""
            });

        services.AddSingleton(mockUserSettingService.Object);
        services.AddHttpClient();
        services.AddTestMarketDataHttpClients();
        services.AddSingleton<BinanceMarketDataService>();
        services.AddLogging();

        // 注册被测试的服务
        services.AddKeyedSingleton<IKLineService, AShareKLineService>(MarketType.AShare);
        services.AddKeyedSingleton<IKLineService, CryptoKLineService>(MarketType.Crypto);

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

    /// <summary>
    /// 验证 K 线数据的真实性：数量、OHLC 关系、收盘价为正
    /// </summary>
    private static void AssertKLineDataValid(List<KLineData> kLineData, int expectedMinCount)
    {
        Assert.IsNotNull(kLineData, "K线数据不应为 null");
        Assert.IsTrue(kLineData.Count >= expectedMinCount, $"K线数据数量应至少为 {expectedMinCount}，实际 {kLineData.Count}");
        Assert.IsTrue(kLineData.All(k => k.Close > 0), "所有K线的收盘价应大于 0");
        Assert.IsTrue(kLineData.All(k => k.High >= k.Close), "所有K线的最高价应不低于收盘价");
        Assert.IsTrue(kLineData.All(k => k.Low <= k.Close), "所有K线的最低价应不高于收盘价");
        Assert.IsTrue(kLineData.All(k => k.High >= k.Low), "所有K线的最高价应不低于最低价");
        Assert.IsTrue(kLineData.All(k => k.Open > 0), "所有K线的开盘价应大于 0");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_AShare_Minute15_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        var kLineData = await service.GetKLineDataAsync("SH600519", KLineType.Minute15, 50);

        // Assert
        AssertKLineDataValid(kLineData, 1);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_AShare_Daily_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        var kLineData = await service.GetKLineDataAsync("SH600519", KLineType.Daily, 100);

        // Assert
        AssertKLineDataValid(kLineData, 1);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_AShare_Weekly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        var kLineData = await service.GetKLineDataAsync("SH600519", KLineType.Weekly, 50);

        // Assert
        AssertKLineDataValid(kLineData, 1);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Minute15_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("BTCUSDT", KLineType.Minute15, 50);

        // Assert
        AssertKLineDataValid(kLineData, 1);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Daily_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("BTCUSDT", KLineType.Daily, 100);

        // Assert
        AssertKLineDataValid(kLineData, 1);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Weekly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("BTCUSDT", KLineType.Weekly, 50);

        // Assert
        AssertKLineDataValid(kLineData, 1);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Monthly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("ETHUSDT", KLineType.Monthly, 30);

        // Assert
        AssertKLineDataValid(kLineData, 1);
    }
}
