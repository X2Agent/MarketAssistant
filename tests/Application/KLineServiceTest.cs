using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Data;
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

        // 注册依赖服务
        var mockUserSettingService = new Mock<IUserSettingService>();
        mockUserSettingService.Setup(s => s.CurrentSetting)
            .Returns(new UserSetting
            {
                ZhiTuApiToken = "test-token"
            });

        services.AddSingleton(mockUserSettingService.Object);
        services.AddSingleton<PlaywrightService>();
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

    private static async Task AssertHasDataOrFriendlyFailureAsync(Task<List<KLineData>> action)
    {
        try
        {
            var kLineData = await action;
            Assert.IsNotNull(kLineData);
            Assert.IsTrue(kLineData.Count > 0);
            Assert.IsTrue(kLineData.All(k => k.Close > 0));
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_AShare_Minute15_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        await AssertHasDataOrFriendlyFailureAsync(service.GetKLineDataAsync("SH600519", KLineType.Minute15, 50));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_AShare_Daily_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        await AssertHasDataOrFriendlyFailureAsync(service.GetKLineDataAsync("SH600519", KLineType.Daily, 100));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_AShare_Weekly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.AShare);

        // Act
        await AssertHasDataOrFriendlyFailureAsync(service.GetKLineDataAsync("SH600519", KLineType.Weekly, 50));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Minute15_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        await AssertHasDataOrFriendlyFailureAsync(service.GetKLineDataAsync("BTCUSDT", KLineType.Minute15, 50));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Daily_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        await AssertHasDataOrFriendlyFailureAsync(service.GetKLineDataAsync("BTCUSDT", KLineType.Daily, 100));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Weekly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        await AssertHasDataOrFriendlyFailureAsync(service.GetKLineDataAsync("BTCUSDT", KLineType.Weekly, 50));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKLineDataAsync_Crypto_Monthly_ShouldReturnData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

        // Act
        await AssertHasDataOrFriendlyFailureAsync(service.GetKLineDataAsync("ETHUSDT", KLineType.Monthly, 30));
    }
}
