using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Tools;

/// <summary>
/// ITechnicalDataTools 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class TechnicalDataToolsTest
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
        services.AddHttpClient();

        // 注册 KLine 服务（TechnicalTools 依赖）
        services.AddKeyedSingleton<IKLineService, AShareKLineService>(MarketType.AShare);
        services.AddKeyedSingleton<IKLineService, CryptoKLineService>(MarketType.Crypto);

        // 注册被测试的服务
        services.AddKeyedSingleton<ITechnicalDataTools, AShareTechnicalTools>(MarketType.AShare);
        services.AddKeyedSingleton<ITechnicalDataTools, CryptoTechnicalTools>(MarketType.Crypto);

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

    #region A股技术数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKDJAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.AShare);

        // Act
        var indicator = await service.GetKDJAsync("SH600519");

        // Assert
        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMACDAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.AShare);

        // Act
        var indicator = await service.GetMACDAsync("SH600519");

        // Assert
        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetBOLLAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.AShare);

        // Act
        var indicator = await service.GetBOLLAsync("SH600519");

        // Assert
        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMAAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.AShare);

        // Act
        var indicator = await service.GetMAAsync("SH600519");

        // Assert
        Assert.IsNotNull(indicator);
    }

    #endregion

    #region 虚拟币技术数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetKDJAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

        // Act
        var indicator = await service.GetKDJAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMACDAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

        // Act
        var indicator = await service.GetMACDAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetBOLLAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

        // Act
        var indicator = await service.GetBOLLAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMAAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

        // Act
        var indicator = await service.GetMAAsync("BTCUSDT");

        // Assert
        Assert.IsNotNull(indicator);
    }

    #endregion
}
