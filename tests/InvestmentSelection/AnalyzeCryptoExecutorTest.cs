using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.InvestmentSelection;

/// <summary>
/// AnalyzeCryptoExecutor 测试（验证正常流程）
/// </summary>
[TestClass]
public class AnalyzeCryptoExecutorTest
{
    private Mock<IChatClientFactory> _mockChatClientFactory = null!;
    private Mock<ILogger<AnalyzeCryptoExecutor>> _mockLogger = null!;
    private AnalyzeCryptoExecutor _executor = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockChatClientFactory = new Mock<IChatClientFactory>();
        _mockLogger = new Mock<ILogger<AnalyzeCryptoExecutor>>();
        _executor = new AnalyzeCryptoExecutor(_mockChatClientFactory.Object, _mockLogger.Object);
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
            new AnalyzeCryptoExecutor(null!, _mockLogger.Object));
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AnalyzeCryptoExecutor(_mockChatClientFactory.Object, null!));
    }
}
