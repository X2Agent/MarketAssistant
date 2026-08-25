using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Agents.Analysts;

namespace TestMarketAssistant.Analysis;

/// <summary>
/// P0-04：分析师必须按市场裁剪——币圈不再出现财务分析师，A 股不出现项目指标分析师。
/// </summary>
[TestClass]
public class SupportedMarketsAttributeTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void FinancialAnalyst_ShouldOnlySupportAShare()
    {
        Assert.IsTrue(SupportedMarketsAttribute.SupportsMarket(typeof(FinancialAnalystAgent), MarketType.AShare));
        Assert.IsFalse(SupportedMarketsAttribute.SupportsMarket(typeof(FinancialAnalystAgent), MarketType.Crypto),
            "财务分析师不应在虚拟币市场运行");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoMetricsAnalyst_ShouldOnlySupportCrypto()
    {
        Assert.IsTrue(SupportedMarketsAttribute.SupportsMarket(typeof(CryptoMetricsAnalystAgent), MarketType.Crypto));
        Assert.IsFalse(SupportedMarketsAttribute.SupportsMarket(typeof(CryptoMetricsAnalystAgent), MarketType.AShare),
            "项目指标分析师不应在 A 股市场运行");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AgentWithoutAttribute_ShouldSupportAllMarkets()
    {
        // FundamentalAnalystAgent 未标注 SupportedMarkets，应默认两个市场都支持
        Assert.IsTrue(SupportedMarketsAttribute.SupportsMarket(typeof(FundamentalAnalystAgent), MarketType.AShare));
        Assert.IsTrue(SupportedMarketsAttribute.SupportsMarket(typeof(FundamentalAnalystAgent), MarketType.Crypto));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Registry_ShouldDiscoverCryptoMetricsAnalyst()
    {
        var types = AnalystTypeRegistry.GetConcreteAnalystTypes();
        Assert.IsTrue(types.Any(t => t.Name == nameof(CryptoMetricsAnalystAgent)),
            "类型注册表应能发现新增的项目指标分析师");
    }
}