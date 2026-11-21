using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

[TestClass]
public sealed class EmergencyStopServiceTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task CloseAllPositions_PositionSnapshotFails_ShouldReturnFailureWithoutClosing()
    {
        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(s => s.CurrentSetting)
            .Returns(new UserSetting { CryptoTradingMode = TradingMode.LiveSpot });
        var environment = new TradingEnvironmentService(
            settingService.Object,
            Mock.Of<IMarketMonitorProvider>(),
            NullLogger<TradingEnvironmentService>.Instance);
        var dataService = new Mock<TradingDataService>(environment, NullLogger<TradingDataService>.Instance);
        dataService.Setup(s => s.LoadTradingHaltAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "已停机", (DateTime?)null));
        var haltState = new TradingHaltState(dataService.Object, NullLogger<TradingHaltState>.Instance);

        var portfolioService = new Mock<CryptoPortfolioService>(
            null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
        portfolioService.Setup(p => p.GetCurrentPositionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network down"));
        var tradeExecutor = new Mock<TradeExecutor>(
            null!, null!, null!, NullLogger<TradeExecutor>.Instance, null!, null!, null!);
        var emergencyStop = new EmergencyStopService(
            Mock.Of<IMarketMonitorProvider>(), null!, null!, portfolioService.Object,
            tradeExecutor.Object, environment, haltState, null!, NullLogger<EmergencyStopService>.Instance);

        var result = await emergencyStop.CloseAllPositionsAsync("测试");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.ClosedSymbolCount);
        Assert.HasCount(1, result.Failures);
        StringAssert.Contains(result.Message, "获取持仓失败");
        tradeExecutor.Verify(e => e.ExecuteEmergencyCloseAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>(), It.IsAny<TradingMode>()),
            Times.Never);
    }
}
