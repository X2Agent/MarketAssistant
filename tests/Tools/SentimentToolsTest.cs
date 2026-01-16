using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Tools;

/// <summary>
/// ISentimentTools 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class SentimentToolsTest
{
    private ServiceProvider? _serviceProvider;
    private ILogger<SentimentToolsTest>? _logger;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册依赖服务
        services.AddSingleton<IUserSettingService, UserSettingService>();
        services.AddLogging();
        services.AddHttpClient();

        // 注册被测试的服务
        services.AddKeyedSingleton<IShareSentimentTools, AShareSentimentTools>(MarketType.AShare);
        services.AddKeyedSingleton<ISentimentTools, AShareSentimentTools>(MarketType.AShare);
        services.AddKeyedSingleton<ICryptoSentimentTools, CryptoSentimentTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ISentimentTools, CryptoSentimentTools>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<SentimentToolsTest>>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    #region A股情绪数据测试

    [TestMethod]
    public async Task GetFundFlowAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareSentimentTools>(MarketType.AShare);

        // Act
        var sentimentData = await service.GetFundFlowAsync("SH600519");

        // Assert
        Assert.IsNotNull(sentimentData);
    }

    #endregion

    #region 虚拟币情绪数据测试

    [TestMethod]
    public async Task GetFundingRateAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var fundingRateHistory = await service.GetFundingRateAsync("BTC");

        // Assert
        Assert.IsNotNull(fundingRateHistory);
        Assert.IsNotNull(fundingRateHistory.Symbol);
        Assert.IsTrue(fundingRateHistory.Symbol.Contains("BTC"), $"期望符号包含 BTC，实际为 {fundingRateHistory.Symbol}");
        Assert.IsTrue(fundingRateHistory.CurrentFundingTime > 0, "当前费率时间应大于 0");
        Assert.IsTrue(fundingRateHistory.NextFundingTime > 0, "下次结算时间应大于 0");
        Assert.IsTrue(fundingRateHistory.NextFundingTime > fundingRateHistory.CurrentFundingTime, "下次结算时间应晚于当前时间");
        Assert.IsNotNull(fundingRateHistory.History);
        Assert.IsTrue(fundingRateHistory.History.Count > 0, "历史数据应至少有 1 条记录");
        Assert.IsTrue(fundingRateHistory.History.Count <= 10, "历史数据不应超过请求的 limit");

        _logger?.LogInformation(
            "当前费率: {CurrentRate}%, 平均费率: {AverageRate}%, 历史记录数: {Count}",
            fundingRateHistory.CurrentRate,
            fundingRateHistory.AverageRate,
            fundingRateHistory.History.Count);
    }

    [TestMethod]
    public async Task GetGlobalLongShortRatioAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetGlobalLongShortRatioAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Symbol);
        Assert.IsTrue(result.History.Count > 0);
    }

    [TestMethod]
    public async Task GetTopTraderAccountRatioAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetTopTraderAccountRatioAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Symbol);
        Assert.IsTrue(result.History.Count > 0);
    }

    [TestMethod]
    public async Task GetTopTraderPositionRatioAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetTopTraderPositionRatioAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Symbol);
        Assert.IsTrue(result.History.Count > 0);
    }

    [TestMethod]
    public async Task GetOpenInterestAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetOpenInterestAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Symbol);
        Assert.IsTrue(result.History.Count > 0);
    }

    #endregion
}
