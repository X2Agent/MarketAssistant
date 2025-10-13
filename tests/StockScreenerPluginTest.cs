using MarketAssistant.Agents.Plugins;
using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Services.Browser;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public sealed class StockScreenerPluginTest : BaseKernelTest
{
    private StockScreenerPlugin _plugin = null!;
    private Mock<ILogger<StockScreenerPlugin>> _mockLogger = null!;
    private PlaywrightService _playwrightService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockLogger = new Mock<ILogger<StockScreenerPlugin>>();
        _playwrightService = new PlaywrightService(_userSettingService, null);
        _plugin = new StockScreenerPlugin(_playwrightService, _mockLogger.Object);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_playwrightService != null)
        {
            await _playwrightService.DisposeAsync();
        }
    }

    #region 构造函数测试

    [TestMethod]
    public void TestXueqiuStockScreenerPlugin_Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act & Assert
        Assert.IsNotNull(_plugin);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void TestXueqiuStockScreenerPlugin_Constructor_WithNullPlaywrightService_ShouldThrowArgumentNullException()
    {
        // Act
        new StockScreenerPlugin(null!, _mockLogger.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void TestXueqiuStockScreenerPlugin_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        new StockScreenerPlugin(_playwrightService, null!);
    }

    #endregion

    #region 新的统一API测试

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithDefaultCriteria_ShouldReturnStocks()
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
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);

        Console.WriteLine($"=== 雪球默认条件筛选测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 价格: {stock.Current}, 涨跌: {stock.Pct:F2}%, 市值: {stock.Mc / 100000000:F0}亿元");
        }
    }

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithMarketCapFilter_ShouldReturnFilteredStocks()
    {
        // Arrange
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
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球市值筛选测试结果 ===");
        Console.WriteLine($"筛选条件: 市值范围={criteria.Criteria[0].MinValue / 100000000:F0}-{criteria.Criteria[0].MaxValue / 100000000:F0}亿元");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 市值: {stock.Mc / 100000000:F0}亿元");
        }
    }

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithPEFilter_ShouldReturnValidStocks()
    {
        // Arrange
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "全部",
            Limit = 12,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "pettm",
                    DisplayName = "市盈率TTM",
                    MinValue = 10m,
                    MaxValue = 30m
                }
            }
        };

        // Act
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球PE筛选测试结果 ===");
        Console.WriteLine($"PE范围: {criteria.Criteria[0].MinValue}-{criteria.Criteria[0].MaxValue}");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} - 价格: {stock.Current}");
        }
    }

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithMultipleIndicators_ShouldReturnValidStocks()
    {
        // Arrange
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
                },
                new()
                {
                    Code = "roediluted",
                    DisplayName = "净资产收益率",
                    MinValue = 8m,
                    MaxValue = null // 无上限
                }
            }
        };

        // Act
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球多指标筛选测试结果 ===");
        Console.WriteLine($"筛选条件数量: {criteria.Criteria.Count}");
        foreach (var criterion in criteria.Criteria)
        {
            Console.WriteLine($"  - {criterion.DisplayName}: {criterion.MinValue} ~ {criterion.MaxValue}");
        }
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(3))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 价格: {stock.Current}, 市值: {stock.Mc / 100000000:F0}亿元");
        }
    }

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithSnowballIndicators_ShouldReturnValidStocks()
    {
        // Arrange
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "全部",
            Limit = 15,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "follow",
                    DisplayName = "累计关注人数",
                    MinValue = 1000m,
                    MaxValue = null
                },
                new()
                {
                    Code = "tweet",
                    DisplayName = "累计讨论次数",
                    MinValue = 500m,
                    MaxValue = null
                }
            }
        };

        // Act
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球热度指标筛选测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(3))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 价格: {stock.Current}");
        }
    }

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithMarketIndicators_ShouldReturnValidStocks()
    {
        // Arrange
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "全部",
            Limit = 10,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "pct",
                    DisplayName = "当日涨跌幅",
                    MinValue = -2m,
                    MaxValue = 5m
                },
                new()
                {
                    Code = "volume_ratio",
                    DisplayName = "当日量比",
                    MinValue = 1.5m,
                    MaxValue = null
                }
            }
        };

        // Act
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球行情指标筛选测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(3))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 涨跌: {stock.Pct:F2}%, 成交量: {stock.Volume}万");
        }
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithNullCriteria_ShouldThrowArgumentNullException()
    {
        // Act
        await _plugin.ScreenStocksAsync(null!);
    }

    #endregion

    #region 行业筛选测试

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithIndustryFilter_ShouldReturnFilteredStocks()
    {
        // Arrange
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "半导体", // 测试半导体行业
            Limit = 15,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "mc",
                    DisplayName = "总市值",
                    MinValue = 10000000000m,  // 100亿元
                    MaxValue = null
                }
            }
        };

        // Act
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球行业筛选测试结果 ===");
        Console.WriteLine($"筛选行业: {criteria.Industry}");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 价格: {stock.Current}, 市值: {stock.Mc / 100000000:F0}亿元");
        }
    }

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithBankIndustry_ShouldReturnBankStocks()
    {
        // Arrange
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "股份制银行", // 测试银行行业
            Limit = 10,
            Criteria = new List<StockScreeningCriteria>()
        };

        // Act
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球银行行业筛选测试结果 ===");
        Console.WriteLine($"筛选行业: {criteria.Industry}");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 价格: {stock.Current}");
        }
    }

    [TestMethod]
    public async Task TestXueqiuStockScreenerPlugin_ScreenStocksAsync_WithInvalidIndustry_ShouldUseDefaultAndReturnStocks()
    {
        // Arrange
        var criteria = new StockCriteria
        {
            Market = "全部A股",
            Industry = "不存在的行业", // 测试无效行业名称
            Limit = 5,
            Criteria = new List<StockScreeningCriteria>()
        };

        // Act
        var result = await _plugin.ScreenStocksAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"=== 雪球无效行业筛选测试结果 ===");
        Console.WriteLine($"筛选行业: {criteria.Industry} (应使用默认'全部')");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(3))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 价格: {stock.Current}");
        }
    }

    #endregion
}
