using MarketAssistant.Agents;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public sealed class StockSelectionTest : BaseKernelTest
{
    private StockSelectionService _stockSelectionService = null!;
    private StockScreeningPlugin _stockScreeningPlugin = null!;
    private StockSelectionManager _stockSelectionManager = null!;

    [TestInitialize]
    public void Initialize()
    {
        // 创建股票筛选插件
        _stockScreeningPlugin = new StockScreeningPlugin(_httpClientFactory, _userSettingService);

        // 创建选股管理器
        var managerLogger = new Mock<ILogger<StockSelectionManager>>();
        _stockSelectionManager = new StockSelectionManager(_kernel, managerLogger.Object);

        // 创建选股服务
        var serviceLogger = new Mock<ILogger<StockSelectionService>>();
        _stockSelectionService = new StockSelectionService(_stockSelectionManager, serviceLogger.Object);
    }

    [TestMethod]
    public async Task TestStockScreeningPlugin_ScreenStocksByMarketCap()
    {
        // Arrange
        decimal minMarketCap = 100; // 100亿
        decimal maxMarketCap = 1000; // 1000亿
        int limit = 10;

        // Act
        var result = await _stockScreeningPlugin.ScreenStocksByMarketCapAsync(minMarketCap, maxMarketCap, limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        foreach (var stock in result)
        {
            Assert.IsTrue(stock.MarketCapitalization >= minMarketCap);
            Assert.IsTrue(stock.MarketCapitalization <= maxMarketCap);
            Assert.IsFalse(string.IsNullOrEmpty(stock.SecurityName));
            Assert.IsFalse(string.IsNullOrEmpty(stock.SecurityCode));
        }

        Console.WriteLine($"筛选出 {result.Count} 只股票（市值范围：{minMarketCap}-{maxMarketCap}亿）");
        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"{stock.SecurityCode} {stock.SecurityName}: 市值 {stock.MarketCapitalization:F2}亿");
        }
    }

    [TestMethod]
    public async Task TestStockScreeningPlugin_ScreenStocksByPE()
    {
        // Arrange
        decimal minPE = 10;
        decimal maxPE = 30;
        int limit = 10;

        // Act
        var result = await _stockScreeningPlugin.ScreenStocksByPEAsync(minPE, maxPE, limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        foreach (var stock in result)
        {
            Assert.IsTrue(stock.PERatio >= minPE);
            Assert.IsTrue(stock.PERatio <= maxPE);
            Assert.IsTrue(stock.PERatio > 0);
        }

        Console.WriteLine($"筛选出 {result.Count} 只股票（PE范围：{minPE}-{maxPE}）");
        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"{stock.SecurityCode} {stock.SecurityName}: PE {stock.PERatio:F2}");
        }
    }

    [TestMethod]
    public async Task TestStockScreeningPlugin_ScreenActiveStocks()
    {
        // Arrange
        decimal minTurnoverRate = 2;
        decimal minAmount = 5; // 5亿
        int limit = 10;

        // Act
        var result = await _stockScreeningPlugin.ScreenActiveStocksAsync(minTurnoverRate, minAmount, limit);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= limit);

        foreach (var stock in result)
        {
            Assert.IsTrue(stock.TurnoverRate >= minTurnoverRate);
            Assert.IsTrue(stock.Amount >= minAmount);
        }

        Console.WriteLine($"筛选出 {result.Count} 只活跃股票（换手率≥{minTurnoverRate}%，成交额≥{minAmount}亿）");
        foreach (var stock in result.Take(5))
        {
            Console.WriteLine($"{stock.SecurityCode} {stock.SecurityName}: 换手率 {stock.TurnoverRate:F2}%, 成交额 {stock.Amount:F2}亿");
        }
    }

    [TestMethod]
    public async Task TestStockSelectionManager_CreateAgent()
    {
        // Act
        var agent = await _stockSelectionManager.CreateStockSelectionAgentAsync();

        // Assert
        Assert.IsNotNull(agent);
        Assert.AreEqual("StockSelectionAgent", agent.Name);

        Console.WriteLine($"成功创建AI选股代理: {agent.Name}");
        Console.WriteLine($"代理描述: {agent.Description}");
    }

    [TestMethod]
    public async Task TestStockSelectionService_QuickSelect_ValueStocks()
    {
        // Act
        var result = await _stockSelectionService.QuickSelectAsync(QuickSelectionStrategy.ValueStocks);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));

        Console.WriteLine("=== 价值股快速选股结果 ===");
        Console.WriteLine(result);
    }

    [TestMethod]
    public async Task TestStockSelectionService_QuickSelect_GrowthStocks()
    {
        // Act
        var result = await _stockSelectionService.QuickSelectAsync(QuickSelectionStrategy.GrowthStocks);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));

        Console.WriteLine("=== 成长股快速选股结果 ===");
        Console.WriteLine(result);
    }

    [TestMethod]
    public async Task TestStockSelectionService_CustomRequirements()
    {
        // Arrange
        var requirements = "请帮我筛选一些适合长期投资的蓝筹股，要求市值大于500亿，PE在10-25之间，ROE大于10%";

        // Act
        var request = new MarketAssistant.Models.StockRecommendationRequest
        {
            UserRequirements = requirements
        };
        var result = await _stockSelectionService.RecommendStocksByUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.AnalysisSummary);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AnalysisSummary));

        Console.WriteLine("=== 自定义需求选股结果 ===");
        Console.WriteLine($"用户需求: {requirements}");
        Console.WriteLine("选股结果:");
        Console.WriteLine(result.AnalysisSummary);
    }

    [TestMethod]
    public void TestStockSelectionService_GetStrategies()
    {
        // Act
        var strategies = _stockSelectionService.GetQuickSelectionStrategies();

        // Assert
        Assert.IsNotNull(strategies);
        Assert.IsTrue(strategies.Count > 0);

        Console.WriteLine("=== 支持的快速选股策略 ===");
        foreach (var strategy in strategies)
        {
            Console.WriteLine($"策略: {strategy.Name}");
            Console.WriteLine($"描述: {strategy.Description}");
            Console.WriteLine($"适用场景: {strategy.Scenario}");
            Console.WriteLine($"风险等级: {strategy.RiskLevel}");
            Console.WriteLine();
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        _stockSelectionManager?.Dispose();
        (_stockSelectionService as StockSelectionService)?.Dispose();
    }
}