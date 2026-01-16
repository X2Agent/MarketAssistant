using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.InvestmentSelection;

/// <summary>
/// GenerateCryptoCriteriaExecutor 测试（验证正常流程）
/// </summary>
[TestClass]
public class GenerateCryptoCriteriaExecutorTest
{
    private Mock<IChatClientFactory> _mockChatClientFactory = null!;
    private Mock<ILogger<GenerateCryptoCriteriaExecutor>> _mockLogger = null!;
    private GenerateCryptoCriteriaExecutor _executor = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockChatClientFactory = new Mock<IChatClientFactory>();
        _mockLogger = new Mock<ILogger<GenerateCryptoCriteriaExecutor>>();
        _executor = new GenerateCryptoCriteriaExecutor(_mockChatClientFactory.Object, _mockLogger.Object);
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
            new GenerateCryptoCriteriaExecutor(null!, _mockLogger.Object));
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new GenerateCryptoCriteriaExecutor(_mockChatClientFactory.Object, null!));
    }
}
