using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

namespace TestMarketAssistant;

[TestClass]
public sealed class AdaptiveCardParserTest
{
    private const string CryptoMetricsJson = """
        {
          "MarketCap": { "MarketCapScore": 8.0, "CirculatingAssessment": "流通率 92%，近期无大额解锁" },
          "Liquidity": { "LiquidityScore": 7.5, "VolumeDistributionNotes": "头部交易所占比约 70%" },
          "VolatilityRisk": { "RiskScore": 8.5, "MaxDrawdown": "近 90 天 -35%" },
          "MarketStructure": { "StructureScore": 6.0, "KeyObservations": "买盘略占优" }
        }
        """;

    /// <summary>A 股红涨绿跌：Adaptive Card 里 Attention 渲染为红（看多）、Good 渲染为绿（看空）。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(OperationRecommendation.Buy, AdaptiveTextColor.Attention)]
    [DataRow(OperationRecommendation.Sell, AdaptiveTextColor.Good)]
    [DataRow(OperationRecommendation.Hold, AdaptiveTextColor.Default)]
    public void TechnicalCard_ShouldColorOperationDirectionRedUpGreenDown(
        OperationRecommendation direction,
        AdaptiveTextColor expectedColor)
    {
        var model = new TechnicalAnalysisResult
        {
            Strategy = new TradingStrategyRecommendation { OperationDirection = direction }
        };

        var card = new TechnicalCardParser().Parse(model);

        var title = Flatten(card.Body).OfType<AdaptiveTextBlock>().Single(block => block.Text.StartsWith("建议:"));
        Assert.AreEqual(expectedColor, title.Color);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(InvestmentRating.StrongBuy, "强烈买入", AdaptiveTextColor.Attention)]
    [DataRow(InvestmentRating.Buy, "买入", AdaptiveTextColor.Attention)]
    [DataRow(InvestmentRating.Hold, "持有", AdaptiveTextColor.Warning)]
    [DataRow(InvestmentRating.Reduce, "减持", AdaptiveTextColor.Good)]
    [DataRow(InvestmentRating.Sell, "卖出", AdaptiveTextColor.Good)]
    [DataRow(InvestmentRating.StrongSell, "强烈卖出", AdaptiveTextColor.Good)]
    public void CoordinatorCard_ShouldColorEveryInvestmentRatingRedUpGreenDown(
        InvestmentRating rating,
        string ratingText,
        AdaptiveTextColor expectedColor)
    {
        var card = new CoordinatorCardParser().Parse(new CoordinatorResult { InvestmentRating = rating });

        var ratingBlock = Flatten(card.Body).OfType<AdaptiveTextBlock>().Single(block => block.Text == ratingText);
        Assert.AreEqual(expectedColor, ratingBlock.Color);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoMetricsCard_ShouldRenderFourMetricsAndRiskBoxWhenVolatilityIsHigh()
    {
        Assert.IsTrue(new CryptoMetricsCardParser().TryParse(CryptoMetricsJson, out var card));

        var elements = Flatten(card!.Body).ToList();
        var texts = elements.OfType<AdaptiveTextBlock>().Select(block => block.Text).ToList();
        string[] expectedTexts =
        [
            "市值规模", "8.0", "流通率 92%，近期无大额解锁",
            "流动性", "7.5", "头部交易所占比约 70%",
            "波动风险", "8.5", "最大回撤：近 90 天 -35%",
            "市场结构", "6.0", "买盘略占优"
        ];
        foreach (var expected in expectedTexts)
        {
            Assert.Contains(expected, texts);
        }

        Assert.Contains(
            element => element is AdaptiveContainer { Style: AdaptiveContainerStyle.Attention },
            elements);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoMetricsCard_ShouldOmitRiskBoxWhenVolatilityIsNotHigh()
    {
        var json = CryptoMetricsJson.Replace("8.5", "7.0");

        Assert.IsTrue(new CryptoMetricsCardParser().TryParse(json, out var card));

        Assert.DoesNotContain(
            element => element is AdaptiveContainer { Style: AdaptiveContainerStyle.Attention },
            Flatten(card!.Body).ToList());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoMetricsCard_ShouldOmitMetricsWithoutScoreAndNote()
    {
        const string json = """{ "MarketCap": {}, "Liquidity": {}, "VolatilityRisk": {}, "MarketStructure": {} }""";

        Assert.IsTrue(new CryptoMetricsCardParser().TryParse(json, out var card));

        // 只剩标题行
        Assert.HasCount(1, card!.Body);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoMetricsCard_ShouldRejectJsonWithoutRequiredKeys()
    {
        Assert.IsFalse(new CryptoMetricsCardParser().TryParse("""{ "MarketCap": {} }""", out _));
    }

    private static IEnumerable<AdaptiveElement> Flatten(IEnumerable<AdaptiveElement> elements)
    {
        foreach (var element in elements)
        {
            yield return element;

            IEnumerable<AdaptiveElement> children = element switch
            {
                AdaptiveContainer container => container.Items,
                AdaptiveColumnSet columnSet => columnSet.Columns,
                _ => []
            };
            foreach (var child in Flatten(children))
            {
                yield return child;
            }
        }
    }
}
