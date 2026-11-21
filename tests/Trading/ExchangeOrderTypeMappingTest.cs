using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Models;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 订单类型到币安 REST 类型字符串的映射测试。
///
/// 这是资金安全相关的回归点：条件单类型串一旦拼错（历史上曾产出 "STOPMARKET"），
/// 交易所会拒单，但代码只记一条 warning，表现为"策略看起来有止损、实际没有"。
/// 该缺陷静默且危险，必须有测试锁死。
/// </summary>
[TestClass]
public sealed class ExchangeOrderTypeMappingTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void ConditionOrderTypes_ShouldUseUnderscoreSeparatedNames()
    {
        // 币安合约条件单类型必须是下划线分隔的全大写
        Assert.AreEqual("STOP_MARKET", BinanceExchangeClient.ToExchangeOrderType(OrderType.StopMarket));
        Assert.AreEqual("TAKE_PROFIT_MARKET", BinanceExchangeClient.ToExchangeOrderType(OrderType.TakeProfitMarket));
        Assert.AreEqual("TRAILING_STOP_MARKET", BinanceExchangeClient.ToExchangeOrderType(OrderType.TrailingStopMarket));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SpotConditionTypes_ShouldUseMarketVariants()
    {
        // 现货条件单是 STOP_LOSS / TAKE_PROFIT：触发后以 MARKET 成交
        Assert.AreEqual("STOP_LOSS", BinanceExchangeClient.ToExchangeOrderType(OrderType.StopLoss));
        Assert.AreEqual("TAKE_PROFIT", BinanceExchangeClient.ToExchangeOrderType(OrderType.TakeProfit));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BasicTypes_ShouldMapDirectly()
    {
        Assert.AreEqual("MARKET", BinanceExchangeClient.ToExchangeOrderType(OrderType.Market));
        Assert.AreEqual("LIMIT", BinanceExchangeClient.ToExchangeOrderType(OrderType.Limit));
        // post-only 类型串仍是 LIMIT：合约由账户服务加 timeInForce=GTX，现货由账户服务改写为 LIMIT_MAKER
        Assert.AreEqual("LIMIT", BinanceExchangeClient.ToExchangeOrderType(OrderType.PostOnlyLimit));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AllOrderTypes_ShouldBeExplicitlyMapped()
    {
        // 新增枚举值却忘记加映射时应立即暴露（switch 会抛异常而不是静默拼错）
        foreach (var type in Enum.GetValues<OrderType>())
        {
            var mapped = BinanceExchangeClient.ToExchangeOrderType(type);
            Assert.IsFalse(string.IsNullOrWhiteSpace(mapped), $"{type} 缺少交易所类型映射");
            Assert.IsTrue(mapped.All(c => char.IsAsciiLetterUpper(c) || c == '_'), $"{type} 映射含非法字符: {mapped}");
        }

        // 多词枚举名必须显式映射：直接用 ToString() 会得到 "STOPMARKET" 这类无下划线拼写
        Assert.AreEqual("STOP_MARKET", BinanceExchangeClient.ToExchangeOrderType(OrderType.StopMarket));
        Assert.AreEqual("STOP_LOSS", BinanceExchangeClient.ToExchangeOrderType(OrderType.StopLoss));
    }
}
