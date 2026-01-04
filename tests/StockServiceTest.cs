using MarketAssistant.Applications.Assets;
using MarketAssistant.Services.Browser;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public class StockServiceTest
{
    private AShareAssetInfoService _assetInfoService;
    private Mock<ILogger<AShareAssetInfoService>> _loggerMock;
    private Mock<PlaywrightService> _playwrightServiceMock;

    [TestInitialize]
    public void Initialize()
    {
        _loggerMock = new Mock<ILogger<AShareAssetInfoService>>();
        _playwrightServiceMock = new Mock<PlaywrightService>();
        _assetInfoService = new AShareAssetInfoService(_loggerMock.Object, _playwrightServiceMock.Object);
    }

    [TestMethod]
    public async Task TestGetHotStocksAsync()
    {
        // Act
        var result = await _assetInfoService.GetHotAssetsAsync();
        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
    }
}
