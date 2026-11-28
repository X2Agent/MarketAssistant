using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.InvestmentSelection.Models;
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
        Assert.Contains("JSON Schema", instructions);
        Assert.Contains("criteria", instructions);
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
        Assert.Contains("JSON Schema", instructions);
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
        Assert.Contains("JSON Schema", instructions);
        Assert.Contains("criteria", instructions);
        Assert.Contains("quoteCurrency", instructions);
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

        Assert.Contains(error => error.Contains("Child.Score", StringComparison.Ordinal), errors);
        Assert.Contains(error => error.Contains("Child.Direction", StringComparison.Ordinal), errors);
    }

    [TestMethod]
    public void StockCriteriaStrategy_ShouldRejectUnsupportedIndicator()
    {
        var strategy = new StockCriteriaGenerationStrategy();
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
            () => strategy.DeserializeCriteria(json));

        Assert.Contains("不支持的指标", exception.Message);
    }

    [TestMethod]
    public void StockCriteriaStrategy_ShouldUseFixedCandidatePool()
    {
        var strategy = new StockCriteriaGenerationStrategy();
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

        var criteria = strategy.DeserializeCriteria(json);

        // 候选池与推荐数量解耦：模型输出的 limit 不影响筛选池大小
        Assert.AreEqual(IScreeningCriteria.CandidatePoolSize, criteria.Limit);
    }

    [TestMethod]
    public void CryptoCriteriaStrategy_ShouldUseFixedCandidatePool()
    {
        var strategy = new CryptoCriteriaGenerationStrategy();
        const string json = """
            {
              "criteria": [
                { "code": "market_cap_rank", "maxValue": 50 }
              ],
              "limit": 5
            }
            """;

        var criteria = strategy.DeserializeCriteria(json);

        Assert.AreEqual(IScreeningCriteria.CandidatePoolSize, criteria.Limit);
    }

    [TestMethod]
    public void CryptoCriteriaStrategy_ShouldRejectInvalidRange()
    {
        var strategy = new CryptoCriteriaGenerationStrategy();
        const string json = """
            {
              "criteria": [
                { "code": "market_cap", "minValue": 100, "maxValue": 10 }
              ],
              "limit": 100
            }
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => strategy.DeserializeCriteria(json));

        Assert.Contains("不能大于最大值", exception.Message);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UserPrompt_ShouldNotCoupleCriteriaGenerationToRecommendationCount()
    {
        var request = new InvestmentSelectionWorkflowRequest { Content = "找低估值蓝筹", MaxRecommendations = 3 };

        var stockPrompt = new StockCriteriaGenerationStrategy().BuildUserPrompt(request);
        var cryptoPrompt = new CryptoCriteriaGenerationStrategy().BuildUserPrompt(request);

        Assert.Contains("找低估值蓝筹", stockPrompt);
        Assert.Contains("股票筛选条件", stockPrompt);
        Assert.Contains("虚拟币筛选条件", cryptoPrompt);
        Assert.DoesNotContain("数量限制", stockPrompt);
        Assert.DoesNotContain("数量限制", cryptoPrompt);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("0.00000345", true, DisplayName = "PEPE 级低价币目标价")]
    [DataRow("0.0000036", true, DisplayName = "SHIB 级低价币止损价")]
    [DataRow("0.00000001", true, DisplayName = "下界本身")]
    [DataRow("0.012", true, DisplayName = "原先下界附近的普通价格")]
    [DataRow("0", false, DisplayName = "0 不是合法价格")]
    public void InvestmentRecommendation_PriceRange_ShouldAcceptLowPriceCoins(string price, bool expectedValid)
    {
        var value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        var recommendation = new InvestmentRecommendation
        {
            Symbol = "PEPE",
            Name = "Pepe",
            RecommendationScore = 70,
            Reason = "24h 成交额 2.1 亿美元、市值排名第 45，7 天涨幅 12.5%，流动性充足；但属于 meme 币，价格波动剧烈，仓位应严格控制。",
            RiskLevel = RiskLevel.High,
            TargetPrice = value,
            StopLoss = value
        };

        var errors = StructuredOutputValidator.Validate(recommendation);

        Assert.AreEqual(expectedValid, errors.Count == 0, string.Join("; ", errors));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InvestmentSelectionResult_Schema_ShouldNotExposeBackfilledFields()
    {
        var instructions = StructuredOutputOptions.AppendSchemaInstructions(
            "base instructions",
            typeof(InvestmentSelectionResult),
            StructuredOutputMode.Text);

        Assert.Contains("recommendations", instructions);
        // 由 Executor 回填的字段不属于 LLM 输出契约
        Assert.DoesNotContain("appliedCriteria", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currentPrice", instructions, StringComparison.OrdinalIgnoreCase);
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

        Assert.HasCount(7, parserRegistrations);
        Assert.Contains(typeof(CoordinatorCardParser), parserRegistrations);
        Assert.Contains(typeof(FinancialCardParser), parserRegistrations);
        Assert.Contains(typeof(FundamentalCardParser), parserRegistrations);
        Assert.Contains(typeof(SentimentCardParser), parserRegistrations);
        Assert.Contains(typeof(NewsCardParser), parserRegistrations);
        Assert.Contains(typeof(TechnicalCardParser), parserRegistrations);
        Assert.Contains(typeof(CryptoMetricsCardParser), parserRegistrations);

        // 转换器消费整条解析器责任链，必须与解析器同容器注册
        Assert.Contains(descriptor => descriptor.ServiceType == typeof(AdaptiveCardConverter), services);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(TradingMode.SpotDemo, TradingMode.LiveSpot, true, true)]
    [DataRow(TradingMode.FuturesTestnet, TradingMode.LiveFutures, true, true)]
    [DataRow(TradingMode.SpotDemo, TradingMode.LiveSpot, false, false)]
    [DataRow(TradingMode.LiveSpot, TradingMode.LiveSpot, true, false)]
    [DataRow(TradingMode.LiveSpot, TradingMode.SpotDemo, true, false)]
    public void TradingEnvironment_ShouldRequireConfirmationOnlyWhenRunningMonitorSwitchesToLive(
        TradingMode currentMode,
        TradingMode targetMode,
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
