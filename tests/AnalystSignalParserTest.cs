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

    /// <summary>模型省略非空枚举字段时的原始输出（其余可用字段保留，用于验证"只丢一个维度"）。</summary>
    private const string TechnicalJsonWithoutRating = """
        {
          "strategy": {
            "targetPriceLow": 45.5,
            "confidencePercentage": 70
          }
        }
        """;

    private const string FundamentalJsonWithoutRatingField = """
        {
          "growthValue": {
            "confidencePercentage": 65
          }
        }
        """;

    private const string SentimentJsonWithoutOperation = """
        {
          "shortTermStrategy": {
            "confidencePercentage": 60
          }
        }
        """;

    private const string NewsJsonWithoutEventNature = """
        {
          "eventAnalysis": {
            "confidencePercentage": 80
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

    /// <summary>
    /// 技术面/基本面/情绪面/新闻面的评级字段都是非空枚举，缺省值恰为 StrongBuy / Buy / MajorPositive。
    /// 模型漏输出时不能把枚举默认值当成真实结论，否则回测与绩效统计会被"清一色强烈买入"污染。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_MissingEnumFields_ShouldKeepNullInsteadOfEnumDefault()
    {
        var technical = AnalystSignalParser.Parse(AnalystNames.Technical, TechnicalJsonWithoutRating);
        Assert.IsNotNull(technical);
        Assert.IsNull(technical.Rating, "技术面缺 technicalRating 时不得写成枚举默认值 StrongBuy");
        Assert.IsNull(technical.Operation, "技术面缺 operationDirection 时不得写成枚举默认值 Buy");
        Assert.AreEqual(45.5m, technical.TargetPriceLow, "缺失评级不应影响其余字段的正常映射");

        var fundamental = AnalystSignalParser.Parse(AnalystNames.Fundamental, FundamentalJsonWithoutRatingField);
        Assert.IsNotNull(fundamental);
        Assert.IsNull(fundamental.Rating, "基本面缺 investmentRating 时不得写成枚举默认值 StrongBuy");

        var sentiment = AnalystSignalParser.Parse(AnalystNames.Sentiment, SentimentJsonWithoutOperation);
        Assert.IsNotNull(sentiment);
        Assert.IsNull(sentiment.Operation, "情绪面缺 operationRecommendation 时不得写成枚举默认值 Buy");

        var news = AnalystSignalParser.Parse(AnalystNames.News, NewsJsonWithoutEventNature);
        Assert.IsNotNull(news);
        Assert.IsNull(news.Rating, "新闻面缺 eventNature 时不得兜底成 Hold");
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
