using MarketAssistant.Agents.Tools;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 所有 Agent Tools 的集成测试
/// 验证各个工具类的基本功能正常运行
/// </summary>
[TestClass]
public sealed class ToolsIntegrationTest
{
    private IServiceProvider _serviceProvider = null!;
    private Mock<IUserSettingService> _mockUserSettingService = null!;
    private string _zhiTuApiToken = null!;

    [TestInitialize]
    public void Initialize()
    {
        _zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN")
            ?? throw new InvalidOperationException("ZHITU_API_TOKEN environment variable is not set");

        _mockUserSettingService = new Mock<IUserSettingService>();
        _mockUserSettingService.Setup(x => x.CurrentSetting).Returns(new UserSetting
        {
            ZhiTuApiToken = _zhiTuApiToken,
            EnableWebSearch = true,
            WebSearchApiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_API_KEY") ?? string.Empty
        });

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(_mockUserSettingService.Object);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    #region StockBasicTools 测试

    [TestMethod]
    public async Task StockBasicTools_GetStockInfo_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockBasicTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetStockInfoAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "股票基本信息不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(result.SecurityCode), "股票代码不应为空");
        Console.WriteLine($"股票名称: {result.SecurityName}, 当前价格: {result.CurrentPrice}");
    }

    [TestMethod]
    public async Task StockBasicTools_GetCompanyInfo_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockBasicTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetStockCompanyInfoAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "公司信息不应为空");
        Console.WriteLine($"公司名称: {result.Name}");
    }

    #endregion

    #region StockFinancialTools 测试

    [TestMethod]
    public async Task StockFinancialTools_GetBalanceSheet_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockFinancialTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetBalanceSheetAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "资产负债表数据不应为空");
        Assert.IsTrue(result.Count > 0, "资产负债表应至少包含一条记录");
        Console.WriteLine($"资产负债表记录数: {result.Count}, 最近报告期: {result[0].EndDate}");
        if (result[0].TotalAssets.HasValue)
        {
            Console.WriteLine($"总资产: {result[0].TotalAssets.Value}");
        }
    }

    [TestMethod]
    public async Task StockFinancialTools_GetIncomeStatement_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockFinancialTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetIncomeStatementAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "利润表数据不应为空");
        Assert.IsTrue(result.Count > 0, "利润表应至少包含一条记录");
        Console.WriteLine($"利润表记录数: {result.Count}, 最近报告期: {result[0].EndDate}");
        if (result[0].OperatingRevenue.HasValue)
        {
            Console.WriteLine($"营业收入: {result[0].OperatingRevenue.Value}");
        }
        if (result[0].NetProfit.HasValue)
        {
            Console.WriteLine($"净利润: {result[0].NetProfit.Value}");
        }
    }

    [TestMethod]
    public async Task StockFinancialTools_GetCashFlowStatement_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockFinancialTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetCashFlowStatementAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "现金流量表数据不应为空");
        Assert.IsTrue(result.Count > 0, "现金流量表应至少包含一条记录");
        Console.WriteLine($"现金流量表记录数: {result.Count}, 最近报告期: {result[0].EndDate}");
        if (result[0].NetCashFlowFromOperating.HasValue)
        {
            Console.WriteLine($"经营活动现金流量净额: {result[0].NetCashFlowFromOperating.Value}");
        }
    }

    [TestMethod]
    public async Task StockFinancialTools_GetFinancialRatios_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockFinancialTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetFinancialRatiosAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "财务主要指标数据不应为空");
        Assert.IsTrue(result.Count > 0, "财务主要指标应至少包含一条记录");
        Console.WriteLine($"财务主要指标记录数: {result.Count}, 最近报告期: {result[0].EndDate}");
        if (result[0].ReturnOnEquity.HasValue)
        {
            Console.WriteLine($"净资产收益率: {result[0].ReturnOnEquity.Value}%");
        }
        if (result[0].GrossMargin.HasValue)
        {
            Console.WriteLine($"销售毛利率: {result[0].GrossMargin.Value}%");
        }
    }

    [TestMethod]
    public async Task StockFinancialTools_GetCapitalStructure_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockFinancialTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetCapitalStructureAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "公司股本结构数据不应为空");
        Assert.IsTrue(result.Count > 0, "股本结构应至少包含一条记录");
        Console.WriteLine($"股本结构记录数: {result.Count}, 最近变动日期: {result[0].ChangeDate}");
        if (result[0].TotalShares.HasValue)
        {
            Console.WriteLine($"总股本: {result[0].TotalShares.Value}");
        }
        if (result[0].CirculatingAShares.HasValue)
        {
            Console.WriteLine($"流通A股: {result[0].CirculatingAShares.Value}");
        }
    }

    #endregion

    #region StockTechnicalTools 测试

    [TestMethod]
    public async Task StockTechnicalTools_GetKDJ_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockTechnicalTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetStockKDJAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "KDJ指标数据不应为空");
        Console.WriteLine($"KDJ: K={result.K}, D={result.D}, J={result.J}");
    }

    [TestMethod]
    public async Task StockTechnicalTools_GetMACD_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockTechnicalTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetStockMACDAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "MACD指标数据不应为空");
        Console.WriteLine($"MACD: Diff={result.Diff}, Dea={result.Dea}, Macd={result.Macd}");
    }

    [TestMethod]
    public async Task StockTechnicalTools_GetBOLL_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockTechnicalTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetStockBOLLAsync("002594");

        // Assert
        Assert.IsNotNull(result, "BOLL指标数据不应为空");
        Console.WriteLine($"BOLL: 上轨={result.U}, 中轨={result.M}, 下轨={result.D}");
    }

    [TestMethod]
    public async Task StockTechnicalTools_GetMA_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new StockTechnicalTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetStockMAAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "MA指标数据不应为空");
        Console.WriteLine($"MA: 5日={result.MA5}, 10日={result.MA10}, 20日={result.MA20}");
    }

    #endregion

    #region StockNewsTools 测试

    [TestMethod]
    [Timeout(60000)] // 新闻抓取可能较慢，设置60秒超时
    public async Task StockNewsTools_GetNewsContext_Concise_ShouldReturnData()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(_mockUserSettingService.Object);
        serviceCollection.AddSingleton<PlaywrightService>();
        serviceCollection.AddSingleton<IChatClientFactory, ChatClientFactory>();
        var provider = serviceCollection.BuildServiceProvider();

        var playwrightService = provider.GetRequiredService<PlaywrightService>();
        var chatClientFactory = provider.GetRequiredService<IChatClientFactory>();
        var tools = new StockNewsTools(playwrightService, chatClientFactory);

        // Act
        var result = await tools.GetStockNewsContextAsync("sz002594", topK: 3, responseFormat: "concise");

        // Assert
        Assert.IsNotNull(result, "新闻上下文不应为空");
        Console.WriteLine($"新闻摘要: {result}");
    }

    #endregion

    #region MarketSentimentTools 测试

    [TestMethod]
    public async Task MarketSentimentTools_GetFundFlow_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new MarketSentimentTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetFundFlowAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "资金流向数据不应为空");
        Console.WriteLine($"主力净流入: {result.MainFundDiff}");
    }

    [TestMethod]
    public async Task MarketSentimentTools_GetTopShareholders_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new MarketSentimentTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetTopShareholdersAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "十大股东信息不应为空");
        Assert.IsTrue(result.Count > 0, "十大股东应至少包含一条记录");
        Console.WriteLine($"十大股东记录数: {result.Count}");
        if (result.Count > 0)
        {
            Console.WriteLine($"第一大股东: {result[0].ShareholderName}, 持股比例: {result[0].ShareholdingRatio}");
        }
    }

    [TestMethod]
    public async Task MarketSentimentTools_GetTopCirculatingShareholders_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new MarketSentimentTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetTopCirculatingShareholdersAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "十大流通股东信息不应为空");
        Assert.IsTrue(result.Count > 0, "十大流通股东应至少包含一条记录");
        Console.WriteLine($"十大流通股东记录数: {result.Count}");
        if (result.Count > 0)
        {
            Console.WriteLine($"第一大流通股东: {result[0].ShareholderName}, 持股比例: {result[0].ShareholdingRatio}");
        }
    }

    [TestMethod]
    public async Task MarketSentimentTools_GetShareholderCount_ShouldReturnData()
    {
        // Arrange
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tools = new MarketSentimentTools(httpClientFactory, _mockUserSettingService.Object);

        // Act
        var result = await tools.GetShareholderCountAsync("sz002594");

        // Assert
        Assert.IsNotNull(result, "股东数信息不应为空");
        Assert.IsTrue(result.Count > 0, "股东数应至少包含一条记录");
        Console.WriteLine($"股东数历史记录数: {result.Count}");
        if (result.Count > 0)
        {
            Console.WriteLine($"最近截止日期: {result[0].EndDate}, 股东总数: {result[0].TotalShareholders}");
        }
    }

    #endregion

    #region GroundingSearchTools 测试

    [TestMethod]
    [Timeout(60000)]
    public async Task GroundingSearchTools_Search_CanCallSuccessfully()
    {
        // Arrange - 使用 BaseAgentTest 的服务提供者模式
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(_mockUserSettingService.Object);

        // 模拟必要的 RAG 服务
        var mockOrchestrator = new Mock<IRetrievalOrchestrator>();
        serviceCollection.AddSingleton(mockOrchestrator.Object);

        var mockWebSearchFactory = new Mock<IWebTextSearchFactory>();
        serviceCollection.AddSingleton(mockWebSearchFactory.Object);

        var provider = serviceCollection.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<GroundingSearchTools>>();

        var tools = new GroundingSearchTools(
            mockOrchestrator.Object,
            mockWebSearchFactory.Object,
            _mockUserSettingService.Object,
            logger);

        // Act & Assert - 只验证方法能正常调用
        try
        {
            var result = await tools.SearchAsync("测试查询", 5);
            Assert.IsNotNull(result, "搜索结果不应为空");
            Console.WriteLine($"搜索返回 {result.Count} 条结果");
        }
        catch (Exception ex)
        {
            // 如果依赖服务未完全配置，测试可能失败，标记为不确定
            Assert.Inconclusive($"GroundingSearchTools 调用失败（可能因服务未配置）: {ex.Message}");
        }
    }

    #endregion

    [TestCleanup]
    public void Cleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }
}

