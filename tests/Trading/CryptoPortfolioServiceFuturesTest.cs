using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// CryptoPortfolioService 合约持仓映射测试：
/// 验证多空归一为绝对数量、方向性浮盈、杠杆 ROE 口径与查询异常兜底。
/// </summary>
[TestClass]
public sealed class CryptoPortfolioServiceFuturesTest
{
    private static Mock<CryptoPortfolioService> CreateService(IExchangeClient exchangeClient)
    {
        // 沿用 AISignalHardBoundaryTest 的部分模拟模式：构造依赖传 null，CallBase 走真实逻辑
        return new Mock<CryptoPortfolioService>(
            exchangeClient, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance)
        {
            CallBase = true
        };
    }

    private static Mock<IExchangeClient> CreateFuturesClient(List<ExchangePosition> positions)
    {
        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(true);
        client.Setup(c => c.GetPositionsAsync(null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(positions);
        return client;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_LongAndShort_ShouldNormalizeQuantity_AndDirectionalPnlWithLeverage()
    {
        var positions = new List<ExchangePosition>
        {
            // 多头：数量 2，开仓 100，标记 110，10 倍杠杆
            new() { Symbol = "BTCUSDT", PositionAmt = 2m, EntryPrice = 100m, MarkPrice = 110m, Leverage = 10m },
            // 空头：数量 -1，开仓 50，标记 45，5 倍杠杆
            new() { Symbol = "ETHUSDT", PositionAmt = -1m, EntryPrice = 50m, MarkPrice = 45m, Leverage = 5m },
            // 数量为 0 与无符号的持仓应被过滤
            new() { Symbol = "ZEROUSDT", PositionAmt = 0m, EntryPrice = 1m, MarkPrice = 1m, Leverage = 1m },
            new() { Symbol = "", PositionAmt = 1m, EntryPrice = 1m, MarkPrice = 1m, Leverage = 1m },
        };
        var service = CreateService(CreateFuturesClient(positions).Object);

        var result = await service.Object.GetCurrentPositionsAsync();

        Assert.AreEqual(2, result.Count, "零数量与空符号持仓应被过滤");

        var longPosition = result.Single(p => p.Symbol == "BTCUSDT");
        Assert.AreEqual(2m, longPosition.Quantity, "多头数量应归一为绝对值");
        Assert.AreEqual(110m, longPosition.CurrentPrice, "标记价格有效时应使用标记价格");
        Assert.AreEqual(20m, longPosition.UnrealizedPnl, "多头浮盈 = (110-100) × 2 × 1");
        Assert.AreEqual(100m, longPosition.UnrealizedPnlPercent, "多头 ROE = 价格涨幅 10% × 10 倍杠杆");

        var shortPosition = result.Single(p => p.Symbol == "ETHUSDT");
        Assert.AreEqual(1m, shortPosition.Quantity, "空头数量应归一为绝对值");
        Assert.AreEqual(5m, shortPosition.UnrealizedPnl, "空头浮盈 = (45-50) × 1 × (-1)");
        Assert.AreEqual(50m, shortPosition.UnrealizedPnlPercent, "空头 ROE = 价格跌幅 10% × 5 倍杠杆 × (-1) 方向");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_ZeroLeverage_ShouldFallBackTo1x()
    {
        var positions = new List<ExchangePosition>
        {
            new() { Symbol = "BTCUSDT", PositionAmt = 1m, EntryPrice = 100m, MarkPrice = 110m, Leverage = 0m },
        };
        var service = CreateService(CreateFuturesClient(positions).Object);

        var result = await service.Object.GetCurrentPositionsAsync();

        var position = result.Single();
        Assert.AreEqual(10m, position.UnrealizedPnlPercent, "交易所未返回杠杆时应按 1 倍兜底，百分比 = (110-100)/100 × 100");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_QueryFails_ShouldReturnEmptyList()
    {
        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(true);
        client.Setup(c => c.GetPositionsAsync(null, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new System.Net.Http.HttpRequestException("network down"));
        var service = CreateService(client.Object);

        var result = await service.Object.GetCurrentPositionsAsync();

        Assert.AreEqual(0, result.Count, "合约持仓查询失败应返回空视图而非抛出");
    }
}
