using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.InvestmentSelection;

/// <summary>
/// GenerateStockCriteriaExecutor 测试（验证正常流程）
/// </summary>
[TestClass]
public class GenerateStockCriteriaExecutorTest
{
    private Mock<IChatClientFactory> _mockChatClientFactory = null!;
    private Mock<ILogger<GenerateStockCriteriaExecutor>> _mockLogger = null!;
    private GenerateStockCriteriaExecutor _executor = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockChatClientFactory = new Mock<IChatClientFactory>();
        _mockLogger = new Mock<ILogger<GenerateStockCriteriaExecutor>>();
        _executor = new GenerateStockCriteriaExecutor(_mockChatClientFactory.Object, _mockLogger.Object);
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
            new GenerateStockCriteriaExecutor(null!, _mockLogger.Object));
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new GenerateStockCriteriaExecutor(_mockChatClientFactory.Object, null!));
    }

    [TestMethod]
    public async Task HandleAsync_WithInvalidMarketType_ShouldThrowException()
    {
        var request = new InvestmentSelectionWorkflowRequest
        {
            MarketType = MarketType.Crypto,
            Content = "测试"
        };

        var mockContext = new Mock<IWorkflowContext>();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _executor.HandleAsync(request, mockContext.Object).AsTask());
    }
}
