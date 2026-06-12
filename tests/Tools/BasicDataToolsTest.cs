using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Tools;

/// <summary>
/// IBasicDataTools 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class BasicDataToolsTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddLogging();
        services.AddHttpClient();

        // 注册被测试的服务（使用子接口）
        services.AddKeyedSingleton<IShareBasicTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<IBasicDataTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<ICryptoBasicTools, CryptoBasicTools>(MarketType.Crypto);
        services.AddKeyedSingleton<IBasicDataTools, CryptoBasicTools>(MarketType.Crypto);

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

    #region A股基础数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetAssetInfoAsync_AShare_ShouldReturnValidQuoteInfo()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareBasicTools>(MarketType.AShare);

        // Act
        var quoteInfo = await service.GetAssetInfoAsync("SH600519");

        // Assert
        Assert.IsNotNull(quoteInfo);
        Assert.IsTrue(quoteInfo.CurrentPrice > 0, "当前价格应大于0");
        Assert.IsFalse(string.IsNullOrEmpty(quoteInfo.SecurityName), "股票名称不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(quoteInfo.SecurityCode), "股票代码不应为空");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetCompanyInfoAsync_AShare_ShouldReturnValidCompanyInfo()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareBasicTools>(MarketType.AShare);

        // Act
        var companyInfo = await service.GetCompanyInfoAsync("SH600519");

        // Assert
        Assert.IsNotNull(companyInfo);
    }

    #endregion

    #region 虚拟币基础数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetAssetInfoAsync_Crypto_ShouldReturnValidQuoteInfo()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoBasicTools>(MarketType.Crypto);

        // Act
        var quoteInfo = await service.GetAssetInfoAsync("BTC");

        // Assert
        Assert.IsNotNull(quoteInfo);
        Assert.IsTrue(quoteInfo.CurrentPrice > 0, "当前价格应大于0");
        Assert.AreEqual("BTC", quoteInfo.SecurityCode);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetProjectInfoAsync_Crypto_ShouldReturnValidInfo()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoBasicTools>(MarketType.Crypto);

        // Act
        var projectInfo = await service.GetProjectInfoAsync("BTC");

        // Assert
        Assert.IsNotNull(projectInfo);
    }

    #endregion
}
