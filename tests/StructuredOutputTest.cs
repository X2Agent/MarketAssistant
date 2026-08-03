using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Providers;
using System.ComponentModel.DataAnnotations;

namespace TestMarketAssistant;

[TestClass]
public sealed class StructuredOutputTest
{
    [TestMethod]
    public void BuildSchemaPromptSection_ShouldRequireSingleJsonObject()
    {
        var prompt = StructuredOutputHelper.BuildSchemaPromptSection(
            typeof(StockCriteria),
            nameof(StockCriteria));

        StringAssert.Contains(prompt, "仅返回一个");
        StringAssert.Contains(prompt, "合法 JSON 对象");
        StringAssert.Contains(prompt, "不得输出 JSON 对象之外的任何内容");
        StringAssert.Contains(prompt, "JSON Schema");
        StringAssert.Contains(prompt, "Criteria");
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
