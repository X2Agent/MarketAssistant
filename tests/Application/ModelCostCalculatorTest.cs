using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Cost;

namespace TestMarketAssistant.Application;

/// <summary>
/// <see cref="ModelCostCalculator"/> 单元测试：成本计算、未定价处理、
/// "provider|model" 精确匹配与展示文本格式。
/// </summary>
[TestClass]
public sealed class ModelCostCalculatorTest
{
    private static AnalysisTokenUsage CreateUsage(params (string Agent, long Input, long Output)[] entries)
    {
        var usage = new AnalysisTokenUsage
        {
            InputTokens = entries.Sum(e => e.Input),
            OutputTokens = entries.Sum(e => e.Output),
            CallCount = entries.Length
        };
        usage.Entries.AddRange(entries.Select(e => new TokenUsageEntry
        {
            AgentName = e.Agent,
            InputTokens = e.Input,
            OutputTokens = e.Output,
            CallCount = 1
        }));
        return usage;
    }

    private static List<ModelPrice> CreatePrices(
        string provider,
        string model,
        decimal inputPrice,
        decimal outputPrice)
        => [new ModelPrice
        {
            ProviderId = provider,
            ModelId = model,
            InputPricePerMillionTokens = inputPrice,
            OutputPricePerMillionTokens = outputPrice
        }];

    [TestMethod]
    [TestCategory("Unit")]
    public void Estimate_WithPrices_ShouldComputeCostPerEntry()
    {
        var usage = CreateUsage(("TechnicalAnalyst", 1_000_000, 100_000));
        var prices = CreatePrices("openai", "gpt-test", 2m, 8m);

        var estimate = ModelCostCalculator.Estimate(usage, prices, _ => "openai|gpt-test");

        Assert.AreEqual(2m + 0.8m, estimate.TotalCost, "成本 = 输入1M×2 + 输出0.1M×8");
        Assert.IsFalse(estimate.HasUnpriced);
        Assert.IsTrue(estimate.HasValue);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Estimate_ResolverReturnsNull_ShouldMarkUnpricedWithoutCost()
    {
        var usage = CreateUsage(("TechnicalAnalyst", 1_000_000, 100_000));
        var prices = CreatePrices("openai", "gpt-test", 2m, 8m);

        var estimate = ModelCostCalculator.Estimate(usage, prices, _ => null);

        Assert.AreEqual(0m, estimate.TotalCost);
        Assert.IsTrue(estimate.HasUnpriced, "无法确定模型时必须标记未定价，不得猜测价格");
        Assert.IsFalse(estimate.Lines[0].IsPriced);
        Assert.AreEqual(string.Empty, ModelCostCalculator.FormatCost(estimate), "无已定价消耗不展示金额");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Estimate_ModelNotInPriceTable_ShouldMarkUnpriced()
    {
        var usage = CreateUsage(("TechnicalAnalyst", 500_000, 50_000));
        var prices = CreatePrices("openai", "gpt-test", 2m, 8m);

        var estimate = ModelCostCalculator.Estimate(usage, prices, _ => "openai|gpt-other");

        Assert.AreEqual(0m, estimate.TotalCost);
        Assert.IsTrue(estimate.HasUnpriced);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Estimate_PartiallyPriced_ShouldSumOnlyPricedLines()
    {
        var usage = CreateUsage(
            ("TechnicalAnalyst", 1_000_000, 0),
            ("FinancialAnalyst", 1_000_000, 0));
        var prices = CreatePrices("openai", "gpt-test", 1m, 1m);

        var estimate = ModelCostCalculator.Estimate(usage, prices,
            agent => agent == "TechnicalAnalyst" ? "openai|gpt-test" : null);

        Assert.AreEqual(1m, estimate.TotalCost, "只累计已定价条目");
        Assert.IsTrue(estimate.HasUnpriced);
        Assert.IsTrue(ModelCostCalculator.FormatCost(estimate).StartsWith("≥ ¥", StringComparison.Ordinal),
            "存在未定价条目时金额应标记为下界");
        Assert.Contains("部分未定价", ModelCostCalculator.FormatCost(estimate));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Estimate_ProviderPrefixMismatch_ShouldFallBackToModelIdMatch()
    {
        var usage = CreateUsage(("TechnicalAnalyst", 1_000_000, 1_000_000));
        var prices = CreatePrices("openai", "gpt-test", 3m, 3m);

        // resolver 只给模型 ID（无 provider 前缀）时按模型 ID 兜底匹配
        var estimate = ModelCostCalculator.Estimate(usage, prices, _ => "gpt-test");

        Assert.AreEqual(6m, estimate.TotalCost);
        Assert.IsFalse(estimate.HasUnpriced);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Estimate_NullOrEmptyUsage_ShouldReturnEmptyEstimate()
    {
        var prices = CreatePrices("openai", "gpt-test", 1m, 1m);

        var estimate = ModelCostCalculator.Estimate(null, prices, _ => "openai|gpt-test");

        Assert.IsFalse(estimate.HasValue);
        Assert.AreEqual(0m, estimate.TotalCost);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Estimate_IncompletePriceRow_ShouldBeIgnored()
    {
        var usage = CreateUsage(("TechnicalAnalyst", 1_000_000, 1_000_000));
        var prices = new List<ModelPrice>
        {
            new() { ProviderId = "openai", ModelId = "", InputPricePerMillionTokens = 1m, OutputPricePerMillionTokens = 1m }
        };

        var estimate = ModelCostCalculator.Estimate(usage, prices, _ => "openai");

        Assert.IsTrue(estimate.HasUnpriced, "缺模型 ID 的残行不得参与匹配");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void FormatCost_SmallAmount_ShouldUseHighPrecision()
    {
        var usage = CreateUsage(("TechnicalAnalyst", 1_000, 500));
        var prices = CreatePrices("openai", "gpt-test", 1m, 1m);

        var estimate = ModelCostCalculator.Estimate(usage, prices, _ => "openai|gpt-test");

        // 0.0015 元 → 四位小数展示
        Assert.AreEqual("约 ¥0.0015", ModelCostCalculator.FormatCost(estimate));
    }
}
