using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Agents.Tools.Models.Technical;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
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
/// ITechnicalDataTools 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public class TechnicalDataToolsTest
{
    private ServiceProvider? _serviceProvider;

    public TestContext? TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        // 从环境变量读取 API 密钥（不在代码中硬编码，避免提交到仓库）
        var zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? "";

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 注册命名 HttpClient（含 BaseAddress 与弹性策略），与生产配置一致
        services.AddNamedMarketHttpClients();

        // 注册虚拟币 KLine 服务依赖的数据服务
        services.AddSingleton<BinanceMarketDataService>();

        // 通过 Mock 注入带真实密钥的 UserSetting（避免依赖本地 Preferences 存储）
        var userSetting = new UserSetting
        {
            ZhiTuApiToken = zhiTuApiToken
        };
        var userSettingServiceMock = new Mock<IUserSettingService>();
        userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(userSetting);
        services.AddSingleton<IUserSettingService>(userSettingServiceMock.Object);

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

        // Assert - 验证真实 KDJ 数据（K、D、J 值非空，证明 API 真实返回而非空对象）
        Assert.IsNotNull(indicator, "KDJ 指标不应为空");
        Assert.IsTrue(indicator.K.HasValue, "K 值不应为空");
        Assert.IsTrue(indicator.D.HasValue, "D 值不应为空");
        Assert.IsTrue(indicator.J.HasValue, "J 值不应为空");
        TestContext?.WriteLine($"SH600519 KDJ: K={indicator.K}, D={indicator.D}, J={indicator.J}, 日期={indicator.T}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMACDAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.AShare);

        // Act
        var indicator = await service.GetMACDAsync("SH600519");

        // Assert - 验证真实 MACD 数据（Ema12、Ema26 非 0，证明 API 真实返回而非默认值）
        Assert.IsNotNull(indicator, "MACD 指标不应为空");
        Assert.AreNotEqual(0m, indicator.Ema12, $"Ema12 不应为 0，实际: {indicator.Ema12}");
        Assert.AreNotEqual(0m, indicator.Ema26, $"Ema26 不应为 0，实际: {indicator.Ema26}");
        TestContext?.WriteLine($"SH600519 MACD: Diff={indicator.Diff}, Dea={indicator.Dea}, Macd={indicator.Macd}, Ema12={indicator.Ema12}, Ema26={indicator.Ema26}, 日期={indicator.T}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetBOLLAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.AShare);

        // Act
        var indicator = await service.GetBOLLAsync("SH600519");

        // Assert - 验证真实 BOLL 数据（U、M、D 非空且满足 U > M > D 的布林带几何关系）
        Assert.IsNotNull(indicator, "BOLL 指标不应为空");
        Assert.IsTrue(indicator.U.HasValue, "U（上轨）不应为空");
        Assert.IsTrue(indicator.M.HasValue, "M（中轨）不应为空");
        Assert.IsTrue(indicator.D.HasValue, "D（下轨）不应为空");
        Assert.IsTrue(indicator.U > indicator.M, $"U({indicator.U}) 应大于 M({indicator.M})");
        Assert.IsTrue(indicator.M > indicator.D, $"M({indicator.M}) 应大于 D({indicator.D})");
        TestContext?.WriteLine($"SH600519 BOLL: U={indicator.U}, M={indicator.M}, D={indicator.D}, 日期={indicator.T}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMAAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.AShare);

        // Act
        var indicator = await service.GetMAAsync("SH600519");

        // Assert - 验证真实 MA 数据（MA5、MA20 非空，证明 API 真实返回而非空对象）
        Assert.IsNotNull(indicator, "MA 指标不应为空");
        Assert.IsTrue(indicator.MA5.HasValue, "MA5 不应为空");
        Assert.IsTrue(indicator.MA20.HasValue, "MA20 不应为空");
        TestContext?.WriteLine($"SH600519 MA: MA5={indicator.MA5}, MA10={indicator.MA10}, MA20={indicator.MA20}, MA30={indicator.MA30}, 日期={indicator.T}");
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

        // Assert - 验证真实 KDJ 数据（K、D、J 值非空，证明 K 线数据真实返回并完成指标计算）
        Assert.IsNotNull(indicator, "KDJ 指标不应为空");
        Assert.IsTrue(indicator.K.HasValue, "K 值不应为空");
        Assert.IsTrue(indicator.D.HasValue, "D 值不应为空");
        Assert.IsTrue(indicator.J.HasValue, "J 值不应为空");
        TestContext?.WriteLine($"BTCUSDT KDJ: K={indicator.K}, D={indicator.D}, J={indicator.J}, 日期={indicator.T}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMACDAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

        // Act
        var indicator = await service.GetMACDAsync("BTCUSDT");

        // Assert - 验证真实 MACD 数据（Ema12、Ema26 非 0，证明 K 线数据真实返回并完成指标计算）
        Assert.IsNotNull(indicator, "MACD 指标不应为空");
        Assert.AreNotEqual(0m, indicator.Ema12, $"Ema12 不应为 0，实际: {indicator.Ema12}");
        Assert.AreNotEqual(0m, indicator.Ema26, $"Ema26 不应为 0，实际: {indicator.Ema26}");
        TestContext?.WriteLine($"BTCUSDT MACD: Diff={indicator.Diff}, Dea={indicator.Dea}, Macd={indicator.Macd}, Ema12={indicator.Ema12}, Ema26={indicator.Ema26}, 日期={indicator.T}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetBOLLAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

        // Act
        var indicator = await service.GetBOLLAsync("BTCUSDT");

        // Assert - 验证真实 BOLL 数据（U、M、D 非空且满足 U > M > D 的布林带几何关系）
        Assert.IsNotNull(indicator, "BOLL 指标不应为空");
        Assert.IsTrue(indicator.U.HasValue, "U（上轨）不应为空");
        Assert.IsTrue(indicator.M.HasValue, "M（中轨）不应为空");
        Assert.IsTrue(indicator.D.HasValue, "D（下轨）不应为空");
        Assert.IsTrue(indicator.U > indicator.M, $"U({indicator.U}) 应大于 M({indicator.M})");
        Assert.IsTrue(indicator.M > indicator.D, $"M({indicator.M}) 应大于 D({indicator.D})");
        TestContext?.WriteLine($"BTCUSDT BOLL: U={indicator.U}, M={indicator.M}, D={indicator.D}, 日期={indicator.T}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetMAAsync_Crypto_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

        // Act
        var indicator = await service.GetMAAsync("BTCUSDT");

        // Assert - 验证真实 MA 数据（MA5、MA20 非空，证明 K 线数据真实返回并完成指标计算）
        Assert.IsNotNull(indicator, "MA 指标不应为空");
        Assert.IsTrue(indicator.MA5.HasValue, "MA5 不应为空");
        Assert.IsTrue(indicator.MA20.HasValue, "MA20 不应为空");
        TestContext?.WriteLine($"BTCUSDT MA: MA5={indicator.MA5}, MA10={indicator.MA10}, MA20={indicator.MA20}, MA30={indicator.MA30}, 日期={indicator.T}");
    }

    #endregion
}
