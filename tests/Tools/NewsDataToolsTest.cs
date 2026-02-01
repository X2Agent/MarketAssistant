using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Tools;

/// <summary>
/// INewsDataTools 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class NewsDataToolsTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddLogging();

        // 注册被测试的服务
        services.AddKeyedSingleton<INewsDataTools, AShareNewsTools>(MarketType.AShare);
        services.AddKeyedSingleton<INewsDataTools, CryptoNewsTools>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_serviceProvider != null)
        {
            _serviceProvider.Dispose();
        }
    }

    #region A股新闻数据测试

    [TestMethod]
    public async Task GetNewsAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<INewsDataTools>(MarketType.AShare);

        // Act
        var newsData = await service.GetNewsAsync("SH600519");

        // Assert
        Assert.IsNotNull(newsData);
    }

    #endregion

    #region 虚拟币新闻数据测试

    [TestMethod]
    public async Task GetNewsAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<INewsDataTools>(MarketType.Crypto);

        // Act
        var newsData = await service.GetNewsAsync("btc");

        // Assert
        Assert.IsNotNull(newsData);
    }

    #endregion
}
