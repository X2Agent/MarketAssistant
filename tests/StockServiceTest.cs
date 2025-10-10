using MarketAssistant.Applications.Stocks;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public class StockServiceTest
{
    private StockService _stockService;
    private Mock<ILogger<StockService>> _loggerMock;
    private Mock<PlaywrightService> _playwrightServiceMock;

    [TestInitialize]
    public void Initialize()
    {
        _loggerMock = new Mock<ILogger<StockService>>();
        _playwrightServiceMock = new Mock<PlaywrightService>();
        _stockService = new StockService(_loggerMock.Object, _playwrightServiceMock.Object);
    }

    [TestMethod]
    public async Task TestGetHotStocksAsync()
    {
        // Act
        var result = await _stockService.GetHotStocksAsync();
        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
    }
}
