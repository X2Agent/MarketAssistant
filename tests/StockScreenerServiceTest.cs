using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.StockScreener;
using MarketAssistant.Services.StockScreener.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 股票筛选服务测试（最小原则：仅验证核心正常流程）
/// </summary>
[TestClass]
public sealed class StockScreenerServiceTest
{
    private StockScreenerService _service = null!;
    private Mock<ILogger<StockScreenerService>> _mockLogger = null!;
    private PlaywrightService _playwrightService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockLogger = new Mock<ILogger<StockScreenerService>>();
        
        // 创建 Mock 的 IUserSettingService (不需要任何真实配置)
        var mockUserSettingService = new Mock<IUserSettingService>();
        mockUserSettingService.Setup(s => s.CurrentSetting)
            .Returns(new UserSetting
            {
                ModelId = "test-model",
                Endpoint = "http://localhost",
                ApiKey = "test-key"
            });
        
        _playwrightService = new PlaywrightService(mockUserSettingService.Object, null);
        _service = new StockScreenerService(_playwrightService, _mockLogger.Object);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_playwrightService != null)
        {
            await _playwrightService.DisposeAsync();
        }
    }

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Assert
        Assert.IsNotNull(_service);
    }

    [TestMethod]
    public async Task ScreenStocksAsync_WithDefaultCriteria_ShouldReturnStocks()
    {
        // Arrange
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "全部",
            Limit = 10,
            Criteria = new List<StockScreeningCriteria>()
        };

        // Act
        var result = await _service.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"默认条件筛选 - 返回股票数量: {result.Count}");
    }

    [TestMethod]
    public async Task ScreenStocksAsync_WithSingleCriteria_ShouldReturnFilteredStocks()
    {
        // Arrange - 测试单个条件筛选（市值）
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "全部",
            Limit = 15,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "mc",
                    DisplayName = "总市值",
                    MinValue = 5000000000m,  // 50亿元
                    MaxValue = 25000000000m  // 250亿元
                }
            }
        };

        // Act
        var result = await _service.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"单条件筛选（市值50-250亿） - 返回股票数量: {result.Count}");
    }

    [TestMethod]
    public async Task ScreenStocksAsync_WithMultipleCriteria_ShouldReturnFilteredStocks()
    {
        // Arrange - 测试多条件组合筛选
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "全部",
            Limit = 20,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "mc",
                    DisplayName = "总市值",
                    MinValue = 50000000000m,   // 500亿元
                    MaxValue = 1000000000000m  // 1万亿元
                },
                new()
                {
                    Code = "pettm",
                    DisplayName = "市盈率TTM",
                    MinValue = 5m,
                    MaxValue = 50m
                }
            }
        };

        // Act
        var result = await _service.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"多条件筛选（市值+PE） - 返回股票数量: {result.Count}");
    }
}

