using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.InvestmentSelection;

/// <summary>
/// ScreenInvestmentTargetsExecutor 测试
/// 验证构造函数参数校验和筛选执行逻辑
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

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IAssetScreenerService)))
            .Returns(_mockScreenerService.Object);

        _executor = new ScreenInvestmentTargetsExecutor(_mockServiceProvider.Object, _mockLogger.Object);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_WithNullServiceProvider_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ScreenInvestmentTargetsExecutor(null!, _mockLogger.Object));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_WithNullLogger_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ScreenInvestmentTargetsExecutor(_mockServiceProvider.Object, null!));
    }
}
