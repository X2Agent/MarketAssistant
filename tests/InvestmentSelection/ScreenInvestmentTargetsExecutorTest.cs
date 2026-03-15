using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.InvestmentSelection;

/// <summary>
/// ScreenInvestmentTargetsExecutor 测试（验证正常流程）
/// </summary>
[TestClass]
public class ScreenInvestmentTargetsExecutorTest
{
    private Mock<IServiceProvider> _mockServiceProvider = null!;
    private Mock<IAssetScreenerService> _mockScreenerService = null!;
    private Mock<ILogger<ScreenInvestmentTargetsExecutor>> _mockLogger = null!;
    private ScreenInvestmentTargetsExecutor _executor = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScreenerService = new Mock<IAssetScreenerService>();
        _mockLogger = new Mock<ILogger<ScreenInvestmentTargetsExecutor>>();

        _executor = new ScreenInvestmentTargetsExecutor(_mockServiceProvider.Object, _mockLogger.Object);
    }

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        Assert.IsNotNull(_executor);
    }

    [TestMethod]
    public void Constructor_WithNullServiceProvider_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ScreenInvestmentTargetsExecutor(null!, _mockLogger.Object));
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ScreenInvestmentTargetsExecutor(_mockServiceProvider.Object, null!));
    }
}
