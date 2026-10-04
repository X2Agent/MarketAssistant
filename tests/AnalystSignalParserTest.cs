using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Extensions.AI;

namespace TestMarketAssistant;

/// <summary>
/// <see cref="AnalystSignalParser"/> 的单元测试：验证各分析师结构化 JSON 到
/// 机器可读信号快照的映射、事件性质到评级的等价映射，以及失败输入的静默降级。
/// </summary>
[TestClass]
public sealed class AnalystSignalParserTest
{
    private const string TechnicalJson = """
        {
          "strategy": {
            "technicalRating": "Buy",
            "operationDirection": "Buy",
            "targetPriceLow": 45.5,
            "targetPriceHigh": 50.25,
            "stopLossPrice": 42.0
          }
        }
        """;

    private const string FundamentalJson = """
        {
          "growthValue": {
            "investmentRating": "StrongBuy"
          }
        }
        """;

    private const string FinancialJsonWithoutRating = """
        {
          "healthAssessment": {},
          "profitQuality": {},
          "cashFlow": {},
          "riskWarning": {}
        }
        """;

    private const string SentimentJson = """
        {
          "shortTermStrategy": {
            "operationRecommendation": "Sell"
          }
        }
        """;

    private const string NewsJson = """
        {
          "eventAnalysis": {
            "eventNature": "MajorPositive"
          }
        }
        """;

    private const string TechnicalJsonWithConfidenceAndHorizon = """
        {
          "strategy": {
            "technicalRating": "Buy",
            "operationDirection": "Buy",
            "holdingPeriod": "MediumTerm",
            "confidencePercentage": 72.5
          }
        }
        """;

    private const string NewsJsonWithConfidenceAndHorizon = """
        {
          "eventAnalysis": {
            "eventNature": "Positive",
            "confidencePercentage": 88
          },
          "impactEvaluation": {
            "impactDuration": "LongTerm"
          }
        }
        """;

    private const string TechnicalJsonWithOutOfRangeConfidence = """
        {
          "strategy": {
            "technicalRating": "Buy",
            "confidencePercentage": 150
          }
        }
        """;

    private const string TechnicalJsonWithNegativeConfidence = """
        {
          "strategy": {
            "technicalRating": "Buy",
            "confidencePercentage": -5
          }
        }
        """;

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_TechnicalAnalystJson_ShouldMapRatingPricesAndStop()
    {
        var snapshot = AnalystSignalParser.Parse(AnalystNames.Technical, TechnicalJson);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(AnalystNames.Technical, snapshot.AnalystName);
        Assert.AreEqual(InvestmentRating.Buy, snapshot.Rating);
        Assert.AreEqual(OperationRecommendation.Buy, snapshot.Operation);
        Assert.AreEqual(45.5m, snapshot.TargetPriceLow);
        Assert.AreEqual(50.25m, snapshot.TargetPriceHigh);
        Assert.AreEqual(42.0m, snapshot.StopLossPrice);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_EventNature_ShouldMapToEquivalentRating()
    {
        var snapshot = AnalystSignalParser.Parse(AnalystNames.News, NewsJson);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(InvestmentRating.StrongBuy, snapshot.Rating);
        Assert.IsNull(snapshot.Operation, "新闻分析师无独立操作方向字段");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_MissingRating_ShouldKeepNullFieldsWithoutInferring()
    {
        var snapshot = AnalystSignalParser.Parse(AnalystNames.Financial, FinancialJsonWithoutRating);

        Assert.IsNotNull(snapshot, "可解析的 JSON 即使缺少评级也应产出快照条目");
        Assert.IsNull(snapshot.Rating, "缺失评级必须保持 null，禁止推断填充");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_LowercaseEnumAndExtraProse_ShouldStillParse()
    {
        var proseWrapped = "结论如下：\n" + FundamentalJson.Replace("StrongBuy", "strongbuy") + "\n以上。";

        var snapshot = AnalystSignalParser.Parse(AnalystNames.Fundamental, proseWrapped);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(InvestmentRating.StrongBuy, snapshot.Rating);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_FailureMarkerOrUnknownAuthorOrNonJson_ShouldReturnNull()
    {
        Assert.IsNull(AnalystSignalParser.Parse(
            AnalystNames.Technical,
            AnalystFailureMessages.BuildFailureText(AnalystNames.Technical, "模型超时")),
            "失败标记不应产出信号");
        Assert.IsNull(AnalystSignalParser.Parse("UnknownAnalyst", TechnicalJson),
            "未知作者不产出信号");
        Assert.IsNull(AnalystSignalParser.Parse(AnalystNames.Technical, "这不是 JSON"),
            "非 JSON 内容不产出信号");
        Assert.IsNull(AnalystSignalParser.Parse(AnalystNames.Coordinator, TechnicalJson),
            "协调分析师信号以 CoordinatorResult 为准，不重复提取");
        Assert.IsNull(AnalystSignalParser.Parse(AnalystNames.CryptoMetrics, TechnicalJson),
            "Crypto 指标分析师无方向性字段，不产出信号");
        Assert.IsNull(AnalystSignalParser.Parse(null, TechnicalJson));
        Assert.IsNull(AnalystSignalParser.Parse(AnalystNames.Technical, null));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_ConfidenceAndHorizon_ShouldMapFromStructuredOutput()
    {
        var technical = AnalystSignalParser.Parse(AnalystNames.Technical, TechnicalJsonWithConfidenceAndHorizon);

        Assert.IsNotNull(technical);
        Assert.AreEqual(72.5f, technical.Confidence, "技术分析师置信度取自策略结论");
        Assert.AreEqual(Duration.MediumTerm, technical.Horizon, "持仓周期直接映射为回测持有窗口");

        var news = AnalystSignalParser.Parse(AnalystNames.News, NewsJsonWithConfidenceAndHorizon);

        Assert.IsNotNull(news);
        Assert.AreEqual(88f, news.Confidence);
        Assert.AreEqual(Duration.LongTerm, news.Horizon, "新闻影响持续时长映射为回测持有窗口");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_MissingConfidenceAndHorizon_ShouldKeepNullWithoutInferring()
    {
        var technical = AnalystSignalParser.Parse(AnalystNames.Technical, TechnicalJson);
        Assert.IsNotNull(technical);
        Assert.IsNull(technical.Confidence, "未给出置信度必须保持 null");
        Assert.IsNull(technical.Horizon, "未给出持仓周期必须保持 null，不得默认为短期");

        var news = AnalystSignalParser.Parse(AnalystNames.News, NewsJson);
        Assert.IsNotNull(news);
        Assert.IsNull(news.Confidence);
        Assert.IsNull(news.Horizon, "未给出影响持续时长必须保持 null");

        Assert.IsNull(AnalystSignalParser.Parse(AnalystNames.Fundamental, FundamentalJson)?.Confidence);
        Assert.IsNull(AnalystSignalParser.Parse(AnalystNames.Sentiment, SentimentJson)?.Confidence);
        Assert.IsNull(AnalystSignalParser.Parse(AnalystNames.Financial, FinancialJsonWithoutRating)?.Confidence);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_ConfidenceOutOfRange_ShouldClampToBucketRange()
    {
        var tooHigh = AnalystSignalParser.Parse(AnalystNames.Technical, TechnicalJsonWithOutOfRangeConfidence);
        Assert.IsNotNull(tooHigh);
        Assert.AreEqual(100f, tooHigh.Confidence, "越界置信度应钳制到 100 而非污染分桶");

        var negative = AnalystSignalParser.Parse(AnalystNames.Technical, TechnicalJsonWithNegativeConfidence);
        Assert.IsNotNull(negative);
        Assert.AreEqual(0f, negative.Confidence, "负置信度应钳制到 0");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ExtractSignals_ShouldSkipSystemAndFailedMessagesInOrder()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, TechnicalJson) { AuthorName = AnalystNames.Technical },
            new(ChatRole.System, "系统指引") { AuthorName = AnalystNames.SystemNotice },
            new(ChatRole.Assistant, AnalystFailureMessages.BuildFailureText(AnalystNames.News, "工具超时"))
                { AuthorName = AnalystNames.News },
            new(ChatRole.Assistant, SentimentJson) { AuthorName = AnalystNames.Sentiment }
        };

        var signals = AnalystSignalParser.ExtractSignals(messages);

        Assert.HasCount(2, signals);
        Assert.AreEqual(AnalystNames.Technical, signals[0].AnalystName);
        Assert.AreEqual(AnalystNames.Sentiment, signals[1].AnalystName);
        Assert.AreEqual(OperationRecommendation.Sell, signals[1].Operation);
    }
}
