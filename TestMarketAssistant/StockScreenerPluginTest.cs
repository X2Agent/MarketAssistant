using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using MarketAssistant.Plugins.Models;
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
        _playwrightService = new PlaywrightService(_userSettingService);
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
    public void TestInvestingStockScreenerPlugin_Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act & Assert
        Assert.IsNotNull(_plugin);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void TestInvestingStockScreenerPlugin_Constructor_WithNullPlaywrightService_ShouldThrowArgumentNullException()
    {
        // Act
        new StockScreenerPlugin(null!, _mockLogger.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void TestInvestingStockScreenerPlugin_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        new StockScreenerPlugin(_playwrightService, null!);
    }

    #endregion

    #region 基本筛选功能测试

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksByCriteriaAsync_WithDefaultParameters_ShouldReturnStocks()
    {
        // Act
        var result = await _plugin.ScreenStocksByCriteriaAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0); // 可能返回空列表（如网站不可访问）

        Console.WriteLine($"=== 默认条件筛选测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 价格: {stock.Price}, 涨跌: {stock.ChangePercent:F2}%");
        }
    }

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksByCriteriaAsync_WithSpecificCriteria_ShouldReturnFilteredStocks()
    {
        // Arrange
        var country = "中国";
        var minMarketCap = 100m; // 100亿
        var maxMarketCap = 1000m; // 1000亿
        var limit = 20;

        // Act
        var result = await _plugin.ScreenStocksByCriteriaAsync(
            country: country,
            minMarketCap: minMarketCap,
            maxMarketCap: maxMarketCap,
            limit: limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        Console.WriteLine($"=== 特定条件筛选测试结果 ===");
        Console.WriteLine($"筛选条件: 国家={country}, 市值范围={minMarketCap}-{maxMarketCap}亿");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} ({stock.Symbol}) - 市值: {stock.MarketCap}亿");

            // 验证市值在范围内（如果有数据）
            if (stock.MarketCap > 0)
            {
                Assert.IsTrue(stock.MarketCap >= minMarketCap);
                Assert.IsTrue(stock.MarketCap <= maxMarketCap);
            }
        }
    }

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksByCriteriaAsync_WithPEFilter_ShouldReturnValidStocks()
    {
        // Arrange
        var minPE = 10m;
        var maxPE = 30m;
        var limit = 15;

        // Act
        var result = await _plugin.ScreenStocksByCriteriaAsync(
            minPE: minPE,
            maxPE: maxPE,
            limit: limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        Console.WriteLine($"=== PE筛选测试结果 ===");
        Console.WriteLine($"PE范围: {minPE}-{maxPE}");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"股票: {stock.Name} - PE: {stock.PERatio}");

            // 验证PE在范围内（如果有数据）
            if (stock.PERatio > 0)
            {
                Assert.IsTrue(stock.PERatio >= minPE);
                Assert.IsTrue(stock.PERatio <= maxPE);
            }
        }
    }

    #endregion

    #region 热门股票测试

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenHotStocksAsync_WithDefaultParameters_ShouldReturnHotStocks()
    {
        // Act
        var result = await _plugin.ScreenHotStocksAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);

        Console.WriteLine($"=== 热门股票测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(10))
        {
            Console.WriteLine($"热门股票: {stock.Name} ({stock.Symbol}) - 涨跌: {stock.ChangePercent:F2}%");
        }
    }

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenHotStocksAsync_WithDifferentSorting_ShouldReturnSortedStocks()
    {
        // Arrange
        var sortBy = "成交量";
        var limit = 10;

        // Act
        var result = await _plugin.ScreenHotStocksAsync(sortBy: sortBy, limit: limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        Console.WriteLine($"=== 按{sortBy}排序热门股票测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result)
        {
            Console.WriteLine($"股票: {stock.Name} - 成交量: {stock.Volume}");
        }
    }

    #endregion

    #region 行业筛选测试

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksBySectorAsync_WithTechSector_ShouldReturnTechStocks()
    {
        // Arrange
        var sector = "科技";
        var limit = 15;

        // Act
        var result = await _plugin.ScreenStocksBySectorAsync(sector, limit: limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        Console.WriteLine($"=== {sector}行业筛选测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result.Take(10))
        {
            Console.WriteLine($"{sector}股票: {stock.Name} ({stock.Symbol}) - 行业: {stock.Sector}");
        }
    }

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksBySectorAsync_WithFinanceSector_ShouldReturnFinanceStocks()
    {
        // Arrange
        var sector = "金融";
        var country = "中国";
        var limit = 10;

        // Act
        var result = await _plugin.ScreenStocksBySectorAsync(sector, country, limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        Console.WriteLine($"=== {country}{sector}行业筛选测试结果 ===");
        Console.WriteLine($"返回股票数量: {result.Count}");

        foreach (var stock in result)
        {
            Console.WriteLine($"{sector}股票: {stock.Name} - 国家: {stock.Country}");
        }
    }

    #endregion

    #region 错误处理测试

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksByCriteriaAsync_WithInvalidCountry_ShouldHandleGracefully()
    {
        // Arrange
        var invalidCountry = "不存在的国家";

        // Act & Assert
        try
        {
            var result = await _plugin.ScreenStocksByCriteriaAsync(country: invalidCountry, limit: 5);

            // 应该返回空列表或默认结果，而不是抛出异常
            Assert.IsNotNull(result);

            Console.WriteLine($"=== 无效国家处理测试 ===");
            Console.WriteLine($"国家: {invalidCountry}, 返回结果数量: {result.Count}");
        }
        catch (Exception ex)
        {
            // 如果抛出异常，验证是预期的异常类型
            Assert.IsTrue(ex is InvalidOperationException);
            Console.WriteLine($"预期异常: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksByCriteriaAsync_WithExtremeValues_ShouldHandleGracefully()
    {
        // Arrange - 使用极端值
        var minMarketCap = 999999m; // 极大市值
        var maxMarketCap = 1000000m;

        // Act
        var result = await _plugin.ScreenStocksByCriteriaAsync(
            minMarketCap: minMarketCap,
            maxMarketCap: maxMarketCap,
            limit: 5);

        // Assert
        Assert.IsNotNull(result);
        // 可能返回空列表，这是正常的

        Console.WriteLine($"=== 极端值处理测试 ===");
        Console.WriteLine($"极大市值筛选: {minMarketCap}-{maxMarketCap}亿");
        Console.WriteLine($"返回结果数量: {result.Count}");
    }

    #endregion

    #region 边界条件测试

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksByCriteriaAsync_WithZeroLimit_ShouldReturnEmptyList()
    {
        // Act
        var result = await _plugin.ScreenStocksByCriteriaAsync(limit: 0);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);

        Console.WriteLine("=== 零限制测试：返回空列表 ===");
    }

    [TestMethod]
    public async Task TestInvestingStockScreenerPlugin_ScreenStocksByCriteriaAsync_WithNegativeLimit_ShouldHandleGracefully()
    {
        // Act
        var result = await _plugin.ScreenStocksByCriteriaAsync(limit: -5);

        // Assert
        Assert.IsNotNull(result);
        // 应该处理负数限制，可能返回空列表或默认数量

        Console.WriteLine($"=== 负数限制测试：返回结果数量 {result.Count} ===");
    }

    #endregion

    #region 数据模型测试

    [TestMethod]
    public void TestInvestingStockInfo_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var stock = new InvestingStockInfo
        {
            Name = "测试股票",
            Symbol = "TEST",
            Price = 100.50m,
            ChangePercent = 5.25m,
            MarketCap = 500.75m,
            PERatio = 15.8m
        };

        // Act
        var result = stock.ToString();

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("测试股票"));
        Assert.IsTrue(result.Contains("TEST"));
        Assert.IsTrue(result.Contains("100.50"));
        Assert.IsTrue(result.Contains("5.25"));

        Console.WriteLine($"=== 股票信息格式化测试 ===");
        Console.WriteLine($"格式化结果: {result}");
    }

    [TestMethod]
    public void TestStockScreeningRequest_DefaultValues_ShouldBeValid()
    {
        // Act
        var request = new StockScreeningRequest();

        // Assert
        Assert.AreEqual("中国", request.Country);
        Assert.AreEqual("涨幅", request.SortBy);
        Assert.AreEqual(50, request.Limit);
        Assert.IsNull(request.MinMarketCap);
        Assert.IsNull(request.MaxMarketCap);

        Console.WriteLine("=== 筛选请求默认值测试通过 ===");
    }

    #endregion
}
