using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Tools;

/// <summary>
/// IShareFinancialTools 接口测试(仅测试 A股 实现,虚拟币已改用 ICryptoMetricsTools)
/// </summary>
[TestClass]
public class FinancialDataToolsTest
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

        // 注册被测试的服务（仅 A股）
        services.AddKeyedSingleton<IShareFinancialTools, AShareFinancialTools>(MarketType.AShare);

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

    #region A股财务数据测试

    [TestMethod]
    public async Task GetBalanceSheetAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act
        var balanceSheets = await service.GetBalanceSheetAsync("SH600519");

        // Assert
        Assert.IsNotNull(balanceSheets);
        Assert.IsTrue(balanceSheets.Count > 0, "应返回至少一条资产负债表数据");
    }

    [TestMethod]
    public async Task GetIncomeStatementAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act
        var incomeStatements = await service.GetIncomeStatementAsync("SH600519");

        // Assert
        Assert.IsNotNull(incomeStatements);
        Assert.IsTrue(incomeStatements.Count > 0, "应返回至少一条利润表数据");
    }

    [TestMethod]
    public async Task GetCashFlowStatementAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act
        var cashFlowStatements = await service.GetCashFlowStatementAsync("SH600519");

        // Assert
        Assert.IsNotNull(cashFlowStatements);
        Assert.IsTrue(cashFlowStatements.Count > 0, "应返回至少一条现金流量表数据");
    }

    [TestMethod]
    public async Task GetFinancialRatiosAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act
        var ratios = await service.GetFinancialRatiosAsync("SH600519");

        // Assert
        Assert.IsNotNull(ratios);
        Assert.IsTrue(ratios.Count > 0, "应返回至少一条财务指标数据");
    }

    [TestMethod]
    public async Task GetCapitalStructureAsync_AShare_ShouldReturnValidData()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act
        var capitalStructure = await service.GetCapitalStructureAsync("SH600519");

        // Assert
        Assert.IsNotNull(capitalStructure);
        Assert.IsTrue(capitalStructure.Count > 0, "应返回至少一条股本结构数据");
    }

    #endregion
}
