using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.AdaptiveCards;
using MarketAssistant.Infrastructure.AdaptiveCards.Parsers;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Providers;
using Microsoft.Extensions.AI;
using MarketAssistant.Services;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;

namespace TestMarketAssistant;

[TestClass]
public sealed class StructuredOutputTest
{
    [TestMethod]
    public void ForJsonSchema_ShouldUseOfficialStructuredOutputFormat()
    {
        var format = ChatResponseFormat.ForJsonSchema<StockCriteria>();

        Assert.IsInstanceOfType<ChatResponseFormatJson>(format);
        Assert.IsTrue(format.Schema.HasValue);
        Assert.AreEqual(nameof(StockCriteria), format.SchemaName);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Text_ShouldUseSchemaPromptWithoutResponseFormat()
    {
        var format = StructuredOutputOptions.CreateResponseFormat(
            typeof(StockCriteria),
            StructuredOutputMode.Text);
        var instructions = StructuredOutputOptions.AppendSchemaInstructions(
            "base instructions",
            typeof(StockCriteria),
            StructuredOutputMode.Text);

        Assert.IsNull(format);
        StringAssert.Contains(instructions, "JSON Schema");
        StringAssert.Contains(instructions, "criteria");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void JsonObject_ShouldUseJsonFormatAndSchemaPrompt()
    {
        var format = StructuredOutputOptions.CreateResponseFormat(
            typeof(StockCriteria),
            StructuredOutputMode.JsonObject);
        var instructions = StructuredOutputOptions.AppendSchemaInstructions(
            "base instructions",
            typeof(StockCriteria),
            StructuredOutputMode.JsonObject);

        Assert.AreSame(ChatResponseFormat.Json, format);
        StringAssert.Contains(instructions, "JSON Schema");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoCriteria_JsonObjectMode_ShouldKeepSchemaConstraints()
    {
        var format = StructuredOutputOptions.CreateResponseFormat(
            typeof(CryptoCriteria),
            StructuredOutputMode.JsonObject);
        var instructions = StructuredOutputOptions.AppendSchemaInstructions(
            "crypto instructions",
            typeof(CryptoCriteria),
            StructuredOutputMode.JsonObject);

        Assert.AreSame(ChatResponseFormat.Json, format);
        StringAssert.Contains(instructions, "JSON Schema");
        StringAssert.Contains(instructions, "criteria");
        StringAssert.Contains(instructions, "quoteCurrency");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void JsonSchema_ShouldUseOfficialFormatWithoutPromptDuplication()
    {
        var format = StructuredOutputOptions.CreateResponseFormat(
            typeof(StockCriteria),
            StructuredOutputMode.JsonSchema);
        var instructions = StructuredOutputOptions.AppendSchemaInstructions(
            "base instructions",
            typeof(StockCriteria),
            StructuredOutputMode.JsonSchema);

        Assert.IsInstanceOfType<ChatResponseFormatJson>(format);
        Assert.AreEqual("base instructions", instructions);
    }

    [TestMethod]
    public void StructuredOutputValidator_ShouldValidateNestedObjectsAndEnums()
    {
        var model = new ValidationRoot
        {
            Child = new ValidationChild
            {
                Score = 11,
                Direction = (ValidationDirection)99
            }
        };

        var errors = StructuredOutputValidator.Validate(model);

        Assert.IsTrue(errors.Any(error => error.Contains("Child.Score", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("Child.Direction", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StockCriteriaStrategy_ShouldRejectUnsupportedIndicator()
    {
        var strategy = new StockCriteriaGenerationStrategy();
        var request = new InvestmentSelectionWorkflowRequest { MaxRecommendations = 5 };
        const string json = """
            {
              "criteria": [
                { "code": "unexpected_metric", "displayName": "非法指标", "minValue": 1 }
              ],
              "market": "AllAShares",
              "industry": "All",
              "limit": 100
            }
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => strategy.DeserializeCriteria(json, request));

        StringAssert.Contains(exception.Message, "不支持的指标");
    }

    [TestMethod]
    public void StockCriteriaStrategy_ShouldUseRequestLimit()
    {
        var strategy = new StockCriteriaGenerationStrategy();
        var request = new InvestmentSelectionWorkflowRequest { MaxRecommendations = 4 };
        const string json = """
            {
              "criteria": [
                { "code": "pettm", "displayName": "市盈率", "maxValue": 30 }
              ],
              "market": "AllAShares",
              "industry": "All",
              "limit": 100
            }
            """;

        var criteria = strategy.DeserializeCriteria(json, request);

        Assert.AreEqual(4, criteria.Limit);
    }

    [TestMethod]
    public void CryptoCriteriaStrategy_ShouldRejectInvalidRange()
    {
        var strategy = new CryptoCriteriaGenerationStrategy();
        var request = new InvestmentSelectionWorkflowRequest { MaxRecommendations = 5 };
        const string json = """
            {
              "criteria": [
                { "code": "market_cap", "minValue": 100, "maxValue": 10 }
              ],
              "limit": 100
            }
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => strategy.DeserializeCriteria(json, request));

        StringAssert.Contains(exception.Message, "不能大于最大值");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InvestmentSelectionExecutors_ShouldRemainMafWorkflowExecutors()
    {
        Assert.IsTrue(typeof(Executor).IsAssignableFrom(typeof(GenerateCriteriaExecutor<StockCriteria>)));
        Assert.IsTrue(typeof(Executor).IsAssignableFrom(typeof(ScreenInvestmentTargetsExecutor)));
        Assert.IsTrue(typeof(Executor).IsAssignableFrom(typeof(AnalyzeAssetsExecutor)));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AddApplicationServices_ShouldRegisterAllAdaptiveCardParsers()
    {
        var services = new ServiceCollection();

        services.AddApplicationServices();

        var parserRegistrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IJsonToAdaptiveCardParser))
            .Select(descriptor => descriptor.ImplementationType)
            .ToHashSet();

        Assert.HasCount(6, parserRegistrations);
        Assert.IsTrue(parserRegistrations.Contains(typeof(CoordinatorCardParser)));
        Assert.IsTrue(parserRegistrations.Contains(typeof(FinancialCardParser)));
        Assert.IsTrue(parserRegistrations.Contains(typeof(FundamentalCardParser)));
        Assert.IsTrue(parserRegistrations.Contains(typeof(SentimentCardParser)));
        Assert.IsTrue(parserRegistrations.Contains(typeof(NewsCardParser)));
        Assert.IsTrue(parserRegistrations.Contains(typeof(TechnicalCardParser)));

        // 转换器消费整条解析器责任链，必须与解析器同容器注册
        Assert.IsTrue(services.Any(descriptor => descriptor.ServiceType == typeof(AdaptiveCardConverter)));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(CryptoTradingMode.BinanceSpotDemo, CryptoTradingMode.LiveSpot, true, true)]
    [DataRow(CryptoTradingMode.BinanceFuturesTestnet, CryptoTradingMode.LiveFutures, true, true)]
    [DataRow(CryptoTradingMode.BinanceSpotDemo, CryptoTradingMode.LiveSpot, false, false)]
    [DataRow(CryptoTradingMode.LiveSpot, CryptoTradingMode.LiveSpot, true, false)]
    [DataRow(CryptoTradingMode.LiveSpot, CryptoTradingMode.BinanceSpotDemo, true, false)]
    public void TradingEnvironment_ShouldRequireConfirmationOnlyWhenRunningMonitorSwitchesToLive(
        CryptoTradingMode currentMode,
        CryptoTradingMode targetMode,
        bool isMonitorRunning,
        bool expected)
    {
        var actual = TradingEnvironmentService.RequiresLiveModeConfirmation(
            currentMode,
            targetMode,
            isMonitorRunning);

        Assert.AreEqual(expected, actual);
    }

    private sealed class ValidationRoot
    {
        public ValidationChild Child { get; init; } = new();
    }

    private sealed class ValidationChild
    {
        [Range(1, 10)]
        public int Score { get; init; }

        public ValidationDirection Direction { get; init; }
    }

    private enum ValidationDirection
    {
        Up,
        Down
    }
}
