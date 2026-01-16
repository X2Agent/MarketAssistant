using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.MultiMarket;

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

        // 注册依赖服务
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<PlaywrightService>();
        services.AddLogging();

        // 注册被测试的服务
        services.AddKeyedSingleton<IKLineService, AShareKLineService>(MarketType.AShare);
        services.AddKeyedSingleton<IKLineService, CryptoKLineService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    [TestMethod]
    public async Task GetKLineDataAsync_AShare_Minute15_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        var kLineData = await service.GetKLineDataAsync("SH600519", KLineType.Minute15, 50);

        // Assert
        Assert.IsNotNull(kLineData);
        Assert.IsTrue(kLineData.Count > 0);
        Assert.IsTrue(kLineData.All(k => k.Close > 0));
    }

    [TestMethod]
    public async Task GetKLineDataAsync_AShare_Daily_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        var kLineData = await service.GetKLineDataAsync("SH600519", KLineType.Daily, 100);

        // Assert
        Assert.IsNotNull(kLineData);
        Assert.IsTrue(kLineData.Count > 0);
        Assert.IsTrue(kLineData.All(k => k.Close > 0 && k.Open > 0));
    }

    [TestMethod]
    public async Task GetKLineDataAsync_AShare_Weekly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        var kLineData = await service.GetKLineDataAsync("SH600519", KLineType.Weekly, 50);

        // Assert
        Assert.IsNotNull(kLineData);
        Assert.IsTrue(kLineData.Count > 0);
    }

    [TestMethod]
    public async Task GetKLineDataAsync_Crypto_Minute15_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("BTCUSDT", KLineType.Minute15, 50);

        // Assert
        Assert.IsNotNull(kLineData);
        Assert.IsTrue(kLineData.Count > 0);
        Assert.IsTrue(kLineData.All(k => k.Close > 0));
    }

    [TestMethod]
    public async Task GetKLineDataAsync_Crypto_Daily_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("BTCUSDT", KLineType.Daily, 100);

        // Assert
        Assert.IsNotNull(kLineData);
        Assert.IsTrue(kLineData.Count > 0);
        Assert.IsTrue(kLineData.All(k => k.Close > 0 && k.Open > 0));
    }

    [TestMethod]
    public async Task GetKLineDataAsync_Crypto_Weekly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("BTCUSDT", KLineType.Weekly, 50);

        // Assert
        Assert.IsNotNull(kLineData);
        Assert.IsTrue(kLineData.Count > 0);
    }

    [TestMethod]
    public async Task GetKLineDataAsync_Crypto_Monthly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        var kLineData = await service.GetKLineDataAsync("ETHUSDT", KLineType.Monthly, 30);

        // Assert
        Assert.IsNotNull(kLineData);
        Assert.IsTrue(kLineData.Count > 0);
    }
}
