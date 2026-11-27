using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.AdaptiveCards.Parsers;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Demo;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AdaptiveCardConverter = MarketAssistant.Infrastructure.AdaptiveCards.AdaptiveCardConverter;

namespace TestMarketAssistant;

/// <summary>
/// 分析师回复到卡片的端到端转换：覆盖 LLM 实际会输出的几种形态（纯 JSON、前置说明文字、代码块），
/// 防止界面再次因为"整条消息必须以 JSON 开头"而把卡片显示成原文。
/// </summary>
[TestClass]
public sealed class AdaptiveCardConverterTest
{
    /// <summary>取自真实运行：基本面分析师在 JSON 之前输出的说明文字。</summary>
    private const string RealWorldPreface =
        "根据工具获取的数据，我已收集到中际旭创的实时行情和公司基本面信息。需要说明的是，" +
        "详细财务报表数据（毛利率、净利率、负债率、现金流等具体数值）未能通过工具获取，以下分析基于可用数据并明确标注缺失项。";

    /// <summary>与线上结构化输出一致：属性名 camelCase，枚举用字符串。</summary>
    private static readonly JsonSerializerOptions LlmJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string FundamentalJson = Serialize(new FundamentalAnalysisResult
    {
        BasicInfo = new StockBasicInfo { Symbol = "sz300308", Name = "中际旭创", CurrentPrice = 912.5m, DailyChangePercent = 1.2f },
        Fundamentals = new CompanyFundamentals { Industry = "光模块", CoreBusiness = "高速光模块研发制造", BusinessQualityScore = 8.5f },
        Competition = new IndustryCompetitiveness { CoreCompetence = "技术与客户壁垒" },
        GrowthValue = new GrowthAndValue { InvestmentRating = InvestmentRating.Buy }
    });

    /// <summary>每位分析师一份最小有效产物，及其卡片 FallbackText 的开头（各解析器互不相同）。</summary>
    private static readonly (string Analyst, string Json, string FallbackPrefix)[] AnalystPayloads =
    [
        ("基本面分析师", FundamentalJson, "基本面分析"),
        ("市场情绪分析师", Serialize(new MarketSentimentAnalysisResult
        {
            SentimentAssessment = new MarketSentiment { EmotionIntensityScore = 7.2f, ConfidenceTrendDescription = "市场信心持续回升" }
        }), "市场情绪分析"),
        ("技术分析师", Serialize(new TechnicalAnalysisResult
        {
            PatternTrend = new ChartPatternTrend { TrendStrengthScore = 7.5f, KeyPatterns = "上升通道内震荡" }
        }), "技术分析报告"),
        ("财务分析师", Serialize(new FinancialAnalysisResult
        {
            HealthAssessment = new FinancialHealth { SolvencyScore = 7f, CoreInsight = "偿债能力稳健，短期压力可控" }
        }), "财务分析报告"),
        ("新闻事件分析师", Serialize(new NewsEventAnalysisResult
        {
            EventAnalysis = new EventInterpretation { EventSummary = "公司发布 800G 光模块量产公告", ImportanceScore = 8f }
        }), "新闻事件分析"),
        ("协调者", Serialize(new CoordinatorResult
        {
            OverallScore = 7.8f,
            InvestmentRating = InvestmentRating.Buy,
            OperationSuggestions = ["逢低分批建仓"]
        }), "综合分析报告"),
        ("虚拟币指标分析师", """
            {"marketCap":{"marketCapScore":8.0,"circulatingAssessment":"流通率 92%"},"liquidity":{"liquidityScore":7.5},
             "volatilityRisk":{"riskScore":6.0,"maxDrawdown":"近 90 天 -35%"},"marketStructure":{"structureScore":6.0}}
            """, "项目指标分析"),
    ];

    public enum OutputShape
    {
        PureJson,
        PrefaceAndJson,
        FencedJson,
        PrefaceAndFencedJson
    }

    public static IEnumerable<object[]> AnalystCardCases()
    {
        foreach (var (analyst, json, fallbackPrefix) in AnalystPayloads)
        {
            foreach (var shape in Enum.GetValues<OutputShape>())
            {
                yield return [analyst, shape, Wrap(json, shape), fallbackPrefix];
            }
        }
    }

    public static string AnalystCardCaseName(MethodInfo methodInfo, object?[]? data)
        => data is [var analyst, var shape, ..] ? $"{analyst} / {shape}" : methodInfo.Name;

    [TestMethod]
    [TestCategory("Unit")]
    [DynamicData(nameof(AnalystCardCases), DynamicDataDisplayName = nameof(AnalystCardCaseName))]
    public void Convert_ShouldRenderCard_ForEveryAnalystInEveryOutputShape(
        string analyst,
        OutputShape shape,
        string text,
        string expectedFallbackPrefix)
    {
        var card = CreateConverter().Convert(text);

        Assert.IsNotNull(card, $"{analyst} 的「{shape}」回复没有渲染成卡片");
        Assert.StartsWith(expectedFallbackPrefix, card.FallbackText);
    }

    public static IEnumerable<object[]> PreviewSampleCases()
    {
        foreach (var sample in CardPreviewSamples.All)
        {
            foreach (var shape in Enum.GetValues<CardPreviewShape>())
            {
                yield return [sample, shape];
            }
        }
    }

    public static string PreviewSampleCaseName(MethodInfo methodInfo, object?[]? data)
        => data is [CardPreviewSample sample, var shape, ..] ? $"{sample.Label} / {shape}" : methodInfo.Name;

    /// <summary>卡片预览页的样本一旦不再被解析器识别，页面上只会看到原文 JSON，这里先拦住。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DynamicData(nameof(PreviewSampleCases), DynamicDataDisplayName = nameof(PreviewSampleCaseName))]
    public void PreviewSamples_ShouldRenderAsCard_InEveryShape(CardPreviewSample sample, CardPreviewShape shape)
    {
        var card = CreateConverter().Convert(CardPreviewSamples.Render(sample, shape));

        Assert.IsNotNull(card, $"预览样本「{sample.Label}」的「{shape}」形态没有渲染成卡片");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("今天大盘震荡，建议观望。")]
    [DataRow("可以参考[这份研报](https://example.com/report)后再决定。")]
    [DataRow("""{"a":1}""")]
    [DataRow("[1,2,3]")]
    public void Convert_ShouldReturnNull_WhenTextHasNoRecognizedResult(string text)
    {
        Assert.IsNull(CreateConverter().Convert(text));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Convert_ShouldLogWarningAndFallBack_WhenRecognizedResultCannotBeParsed()
    {
        // 必需键齐全但 basicInfo 的形状不对：确属基本面产物，反序列化却会失败
        const string malformed = """{"basicInfo":"oops","fundamentals":{},"competition":{}}""";
        var logger = new CapturingLogger<AdaptiveCardConverter>();

        var card = CreateConverter(logger).Convert(malformed);

        Assert.IsNull(card);
        Assert.HasCount(1, logger.Entries);
        Assert.AreEqual(LogLevel.Warning, logger.Entries[0].Level);
        Assert.IsInstanceOfType<JsonException>(logger.Entries[0].Exception);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ChatMessageAdapter_ShouldRenderCard_WhenReplyHasPrefaceBeforeJson()
    {
        var text = Wrap(FundamentalJson, OutputShape.PrefaceAndJson);
        var message = new ChatMessage(ChatRole.Assistant, text) { AuthorName = "FundamentalAnalyst" };

        var adapter = new ChatMessageAdapter(message, CreateConverter());

        Assert.IsTrue(adapter.IsAdaptiveCard);
        Assert.IsFalse(adapter.ShowRichText);
        // 卡片只是展示形态，原文保持不变，复制仍能拿到全文
        Assert.AreEqual(text, adapter.Content);
        Assert.IsTrue(adapter.CanCopy);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ChatMessageAdapter_ShouldRenderCardOnlyAfterStreamCompletes()
    {
        var adapter = new ChatMessageAdapter(
            new ChatMessage(ChatRole.Assistant, "") { AuthorName = "市场分析助手" },
            CreateConverter())
        {
            Status = MessageStatus.Streaming
        };

        adapter.Content = Wrap(FundamentalJson, OutputShape.PrefaceAndJson);
        Assert.IsFalse(adapter.IsAdaptiveCard, "流式期间不应解析");

        adapter.Status = MessageStatus.Sent;
        Assert.IsTrue(adapter.IsAdaptiveCard);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ChatMessageAdapter_ShouldKeepRichText_WhenReplyHasNoRecognizedResult()
    {
        var message = new ChatMessage(ChatRole.Assistant, "今天大盘震荡，建议观望。") { AuthorName = "市场分析助手" };

        var adapter = new ChatMessageAdapter(message, CreateConverter());

        Assert.IsFalse(adapter.IsAdaptiveCard);
        Assert.IsTrue(adapter.ShowRichText);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void FinancialCard_ShouldShowNotAvailableInsteadOfBarPercentSign_WhenPercentFieldsAreMissing()
    {
        // 提示词约定"无数据时设为 null"，所有百分比字段留空
        var model = new FinancialAnalysisResult
        {
            HealthAssessment = new FinancialHealth { SolvencyScore = 7f, CoreInsight = "偿债能力稳健，短期压力可控" },
            ProfitQuality = new ProfitabilityQuality { ProfitSustainability = "利润来源稳定，可持续性较好" }
        };

        var texts = CollectTexts(new FinancialCardParser().Parse(model));

        Assert.DoesNotContain("%", texts);
        Assert.Contains("N/A", texts);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void FinancialCard_ShouldFormatPercentValues_WhenFieldsAreProvided()
    {
        var model = new FinancialAnalysisResult
        {
            HealthAssessment = new FinancialHealth { DebtRatio = 55.5f },
            ProfitQuality = new ProfitabilityQuality { ROE = 18.25f, ROA = 9f, GrossMargin = 32.1f, NetMargin = 12f }
        };

        var texts = CollectTexts(new FinancialCardParser().Parse(model));

        foreach (var value in new[] { 55.5f, 18.25f, 9f, 32.1f, 12f })
        {
            Assert.Contains($"{value:F2}%", texts);
        }
    }

    private static string Serialize<T>(T model) => JsonSerializer.Serialize(model, LlmJsonOptions);

    private static string Wrap(string json, OutputShape shape) => shape switch
    {
        OutputShape.PureJson => json,
        OutputShape.PrefaceAndJson => $"{RealWorldPreface}\n\n{json}",
        OutputShape.FencedJson => $"```json\n{json}\n```",
        OutputShape.PrefaceAndFencedJson => $"{RealWorldPreface}\n\n```json\n{json}\n```",
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
    };

    /// <summary>与生产注册顺序一致的解析器链。</summary>
    internal static AdaptiveCardConverter CreateConverter(ILogger<AdaptiveCardConverter>? logger = null) => new(
        [
            new CoordinatorCardParser(),
            new FinancialCardParser(),
            new FundamentalCardParser(),
            new SentimentCardParser(),
            new NewsCardParser(),
            new TechnicalCardParser(),
            new CryptoMetricsCardParser()
        ],
        logger ?? NullLogger<AdaptiveCardConverter>.Instance);

    /// <summary>收集卡片里所有可见文本：文本块与 FactSet 的取值。</summary>
    private static List<string> CollectTexts(AdaptiveCard card)
    {
        var texts = new List<string>();
        foreach (var element in AdaptiveCardParserTest.Flatten(card.Body))
        {
            switch (element)
            {
                case AdaptiveTextBlock block:
                    texts.Add(block.Text);
                    break;
                case AdaptiveFactSet facts:
                    texts.AddRange(facts.Facts.Select(fact => fact.Value));
                    break;
            }
        }

        return texts;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
