using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public class StockKLineServiceTest
{
    private AShareKLineService _stockKLineService = null!;
    private Mock<IUserSettingService> _mockUserSettingService = null!;

    [TestInitialize]
    public void Initialize()
    {
        var zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? throw new InvalidOperationException("ZHITU_API_TOKEN environment variable is not set");

        // 创建模拟的用户设置服�?
        _mockUserSettingService = new Mock<IUserSettingService>();
        var testUserSetting = new UserSetting
        {
            ZhiTuApiToken = zhiTuApiToken
        };
        _mockUserSettingService.Setup(x => x.CurrentSetting).Returns(testUserSetting);

        // 使用NullLogger和模拟的用户设置服务创建AShareKLineService实例
        _stockKLineService = new AShareKLineService(
            NullLogger<AShareKLineService>.Instance,
            _mockUserSettingService.Object);
    }

    [TestMethod]
    public async Task GetDailyKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "600000";

        // Act
        var result = await _stockKLineService.GetDailyKLineDataAsync(symbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result[0].Timestamp != default);
    }

    [TestMethod]
    public async Task GetWeeklyKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "000001.SZ";

        // Act
        var result = await _stockKLineService.GetWeeklyKLineDataAsync(symbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result[0].Timestamp != default);
    }

    [TestMethod]
    public async Task GetMonthlyKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "601398.SH";

        // Act
        var result = await _stockKLineService.GetMonthlyKLineDataAsync(symbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result[0].Timestamp != default);
    }

    [TestMethod]
    public async Task Get5MinuteKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "600000.SH";

        // Act
        var result = await _stockKLineService.Get5MinuteKLineDataAsync(symbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result[0].Timestamp != default);
    }
}