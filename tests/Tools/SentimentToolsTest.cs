using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
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
/// ISentimentTools 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class SentimentToolsTest
{
    private ServiceProvider? _serviceProvider;
    private ILogger<SentimentToolsTest>? _logger;
    private string? _zhiTuApiToken;

    public TestContext? TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        // 从环境变量读取智兔 API 令牌（不在代码中硬编码，避免提交到仓库）
        _zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN");

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 注册命名 HttpClient（含 BaseAddress 与弹性策略），与生产配置一致
        // AShareSentimentTools 依赖 "Cls" 与 "ZhiTu" 命名 HttpClient
        services.AddNamedMarketHttpClients();

        // CryptoSentimentTools 依赖 BinanceMarketDataService
        services.AddSingleton<BinanceMarketDataService>();

        // 通过 Mock 注入带真实 ZhiTuApiToken 的 UserSetting（避免依赖本地 Preferences 存储）
        var userSetting = new UserSetting
        {
            ZhiTuApiToken = _zhiTuApiToken ?? ""
        };
        var userSettingServiceMock = new Mock<IUserSettingService>();
        userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(userSetting);
        services.AddSingleton<IUserSettingService>(userSettingServiceMock.Object);

        // 注册被测试的服务
        services.AddKeyedSingleton<IShareSentimentTools, AShareSentimentTools>(MarketType.AShare);
        services.AddKeyedSingleton<ISentimentTools, AShareSentimentTools>(MarketType.AShare);
        services.AddKeyedSingleton<ICryptoSentimentTools, CryptoSentimentTools>(MarketType.Crypto);
        services.AddKeyedSingleton<ISentimentTools, CryptoSentimentTools>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<SentimentToolsTest>>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    #region A股情绪数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetFundFlowAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareSentimentTools>(MarketType.AShare);

        // Act
        var sentimentData = await service.GetFundFlowAsync("SH600519");

        // Assert - 验证真实资金流向数据（关键字段非空 + 数值合理性，证明 API 真实返回而非空对象）
        Assert.IsNotNull(sentimentData, "资金流向数据不应为空");
        Assert.IsTrue(sentimentData.Date > 0, $"日期应大于 0，实际: {sentimentData.Date}");
        Assert.IsTrue(sentimentData.MainFundIn > 0 || sentimentData.MainFundOut > 0,
            $"主力流入({sentimentData.MainFundIn})或主力流出({sentimentData.MainFundOut})应至少有一个大于 0");
        Assert.IsTrue(sentimentData.MainFundDiff != 0 || sentimentData.SuperFundDiff != 0 || sentimentData.LargeFundDiff != 0,
            $"主力净流入({sentimentData.MainFundDiff})、超大单净流入({sentimentData.SuperFundDiff})、大单净流入({sentimentData.LargeFundDiff})应至少有一个非零");
        // 主力 = 特大单 + 大单，勾稽关系：MainFundDiff = SuperFundDiff + LargeFundDiff
        var expectedMainDiff = sentimentData.SuperFundDiff + sentimentData.LargeFundDiff;
        Assert.IsTrue(Math.Abs(expectedMainDiff - sentimentData.MainFundDiff) < 1m,
            $"主力净流入({sentimentData.MainFundDiff})应等于超大单({sentimentData.SuperFundDiff})+大单({sentimentData.LargeFundDiff})={expectedMainDiff}");

        TestContext?.WriteLine($"SH600519 日期: {sentimentData.Date}, 主力净流入: {sentimentData.MainFundDiff}, " +
            $"超大单: {sentimentData.SuperFundDiff}, 大单: {sentimentData.LargeFundDiff}, " +
            $"中单: {sentimentData.MediumFundDiff}, 小单: {sentimentData.LittleFundDiff}, " +
            $"3日主力: {sentimentData.MainFund3}, 5日主力: {sentimentData.MainFund5}");
    }

    #endregion

    #region 虚拟币情绪数据测试

    [TestMethod]
    [TestCategory("Integration")]
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
        Assert.IsTrue(fundingRateHistory.History.Count <= 30, "历史数据不应超过请求的 limit");

        _logger?.LogInformation(
            "当前费率: {CurrentRate}%, 平均费率: {AverageRate}%, 历史记录数: {Count}",
            fundingRateHistory.CurrentRate,
            fundingRateHistory.AverageRate,
            fundingRateHistory.History.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetGlobalLongShortRatioAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetGlobalLongShortRatioAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Symbol.Contains("BTC"), $"Symbol 应包含 BTC，实际: {result.Symbol}");
        Assert.IsTrue(result.CurrentLongRatio > 0, $"当前多头占比应 > 0，实际: {result.CurrentLongRatio}");
        Assert.IsTrue(result.CurrentShortRatio > 0, $"当前空头占比应 > 0，实际: {result.CurrentShortRatio}");
        // 多头 + 空头占比应接近 100%
        Assert.IsTrue(Math.Abs(result.CurrentLongRatio + result.CurrentShortRatio - 100m) < 1m,
            $"多头({result.CurrentLongRatio}) + 空头({result.CurrentShortRatio}) 占比应接近 100%");
        Assert.IsTrue(result.History.Count > 0, "历史数据不应为空");

        // 验证历史数据点的不变式：多空占比互补 + 时间戳递增有效
        foreach (var point in result.History)
        {
            Assert.IsTrue(point.LongRatio > 0 && point.ShortRatio > 0,
                $"历史点多空占比应 > 0，Long={point.LongRatio}, Short={point.ShortRatio}");
            Assert.IsTrue(Math.Abs(point.LongRatio + point.ShortRatio - 100m) < 1m,
                $"历史点多空占比应互补，Long={point.LongRatio}, Short={point.ShortRatio}");
            Assert.IsTrue(point.Timestamp > 0, $"历史点时间戳应 > 0，实际: {point.Timestamp}");
        }

        _logger?.LogInformation("全球多空比: {Symbol} 当前多={Long}% 空={Short}% 比率={Ratio} 历史={Count}",
            result.Symbol, result.CurrentLongRatio, result.CurrentShortRatio, result.CurrentRatio, result.History.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetTopTraderAccountRatioAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetTopTraderAccountRatioAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Symbol.Contains("BTC"), $"Symbol 应包含 BTC，实际: {result.Symbol}");
        Assert.IsTrue(result.CurrentLongRatio > 0 && result.CurrentShortRatio > 0,
            $"头部账户多空占比应 > 0，多={result.CurrentLongRatio}, 空={result.CurrentShortRatio}");
        Assert.IsTrue(Math.Abs(result.CurrentLongRatio + result.CurrentShortRatio - 100m) < 1m,
            $"头部账户多空占比应接近 100%，多={result.CurrentLongRatio}, 空={result.CurrentShortRatio}");
        Assert.IsTrue(result.History.Count > 0, "历史数据不应为空");

        foreach (var point in result.History)
        {
            Assert.IsTrue(point.LongRatio > 0 && point.ShortRatio > 0,
                $"历史点多空占比应 > 0，Long={point.LongRatio}, Short={point.ShortRatio}");
        }

        _logger?.LogInformation("头部账户多空比: {Symbol} 多={Long}% 空={Short}% 历史={Count}",
            result.Symbol, result.CurrentLongRatio, result.CurrentShortRatio, result.History.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetTopTraderPositionRatioAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetTopTraderPositionRatioAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Symbol.Contains("BTC"), $"Symbol 应包含 BTC，实际: {result.Symbol}");
        Assert.IsTrue(result.CurrentLongRatio > 0 && result.CurrentShortRatio > 0,
            $"头部持仓多空占比应 > 0，多={result.CurrentLongRatio}, 空={result.CurrentShortRatio}");
        Assert.IsTrue(Math.Abs(result.CurrentLongRatio + result.CurrentShortRatio - 100m) < 1m,
            $"头部持仓多空占比应接近 100%，多={result.CurrentLongRatio}, 空={result.CurrentShortRatio}");
        Assert.IsTrue(result.History.Count > 0, "历史数据不应为空");

        foreach (var point in result.History)
        {
            Assert.IsTrue(point.LongRatio > 0 && point.ShortRatio > 0,
                $"历史点多空占比应 > 0，Long={point.LongRatio}, Short={point.ShortRatio}");
        }

        _logger?.LogInformation("头部持仓多空比: {Symbol} 多={Long}% 空={Short}% 历史={Count}",
            result.Symbol, result.CurrentLongRatio, result.CurrentShortRatio, result.History.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetOpenInterestAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ICryptoSentimentTools>(MarketType.Crypto);

        // Act
        var result = await service.GetOpenInterestAsync("BTC");

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Symbol.Contains("BTC"), $"Symbol 应包含 BTC，实际: {result.Symbol}");
        Assert.IsTrue(result.CurrentOpenInterest > 0, $"当前持仓量应 > 0，实际: {result.CurrentOpenInterest}");
        Assert.IsTrue(result.CurrentOpenInterestValue > 0, $"当前持仓价值应 > 0，实际: {result.CurrentOpenInterestValue}");
        Assert.IsTrue(result.CurrentTimestamp > 0, $"当前时间戳应 > 0，实际: {result.CurrentTimestamp}");
        Assert.IsTrue(result.History.Count > 0, "历史数据不应为空");

        // 验证历史数据点：持仓量与持仓价值均 > 0
        foreach (var point in result.History)
        {
            Assert.IsTrue(point.SumOpenInterest > 0, $"历史点持仓量应 > 0，实际: {point.SumOpenInterest}");
            Assert.IsTrue(point.SumOpenInterestValue > 0, $"历史点持仓价值应 > 0，实际: {point.SumOpenInterestValue}");
            Assert.IsTrue(point.Timestamp > 0, $"历史点时间戳应 > 0，实际: {point.Timestamp}");
        }

        _logger?.LogInformation("持仓量: {Symbol} 当前={OI} 价值={Value} 历史={Count}",
            result.Symbol, result.CurrentOpenInterest, result.CurrentOpenInterestValue, result.History.Count);
    }

    #endregion
}
