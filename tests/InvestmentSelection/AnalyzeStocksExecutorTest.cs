using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.InvestmentSelection;

/// <summary>
/// AnalyzeStocksExecutor 测试（验证正常流程）
/// </summary>
[TestClass]
public class AnalyzeStocksExecutorTest
{
    private Mock<IChatClientFactory> _mockChatClientFactory = null!;
    private Mock<ILogger<AnalyzeStocksExecutor>> _mockLogger = null!;
    private AnalyzeStocksExecutor _executor = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockChatClientFactory = new Mock<IChatClientFactory>();
        _mockLogger = new Mock<ILogger<AnalyzeStocksExecutor>>();
        _executor = new AnalyzeStocksExecutor(_mockChatClientFactory.Object, _mockLogger.Object);
    }

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        Assert.IsNotNull(_executor);
    }

    [TestMethod]
    public void Constructor_WithNullChatClientFactory_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AnalyzeStocksExecutor(null!, _mockLogger.Object));
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AnalyzeStocksExecutor(_mockChatClientFactory.Object, null!));
    }

    [TestMethod]
    public async Task HandleAsync_WithInvalidMarketType_ShouldThrowException()
    {
        var input = new AssetScreeningResult
        {
            ScreenedAssets = new List<ScreenerStockInfo>(),
            OriginalRequest = new InvestmentSelectionWorkflowRequest
            {
                MarketType = MarketType.Crypto
            }
        };

        var mockContext = new Mock<IWorkflowContext>();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _executor.HandleAsync(input, mockContext.Object).AsTask());
    }
}
