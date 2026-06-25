using MarketAssistant.Agents.MarketAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// 市场分析工作流测试
/// 验证核心工作流功能、报告结构完整性和质量指标
/// </summary>
[TestClass]
public sealed class MarketAnalysisWorkflowTest : BaseAgentTest
{
    private MarketAnalysisWorkflow _workflow = null!;

    [TestInitialize]
    public void Initialize()
    {
        RequireLlm();
        _workflow = _serviceProvider.GetRequiredService<MarketAnalysisWorkflow>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        // MarketAnalysisWorkflow 为 DI 单例，无需手动 Dispose
    }

    [TestMethod]
    [TestCategory("E2E")]
    public async Task AnalyzeAsync_ShouldReturnValidReport()
    {
        string assetSymbol = "000001";

        var report = await _workflow.AnalyzeAsync(assetSymbol);

        Assert.IsNotNull(report);
        Assert.AreEqual(assetSymbol, report.AssetSymbol);
        Assert.IsNotNull(report.AnalystMessages);
        Assert.IsTrue(report.AnalystMessages.Count > 0, "应该至少有一位分析师的结果");

        Assert.IsNotNull(report.CoordinatorResult, "协调分析师应该生成结果");
        Assert.IsFalse(string.IsNullOrWhiteSpace(report.CoordinatorResult.Summary), "协调分析师应该生成总结报告");
        Assert.IsTrue(report.CoordinatorResult.Summary.Length >= 50, "总结报告应有足够的深度");

        Assert.IsTrue(
            report.CoordinatorResult.OverallScore is >= 1 and <= 10,
            $"投资评分应在 1-10 范围内，实际值: {report.CoordinatorResult.OverallScore}");
    }

    [TestMethod]
    [TestCategory("E2E")]
    public async Task AnalyzeAsync_ShouldTriggerProgressEvents()
    {
        string assetSymbol = "000001";
        var progressEvents = new List<string>();

        _workflow.ProgressChanged += (sender, e) =>
        {
            progressEvents.Add(e.StageDescription);
        };

        var report = await _workflow.AnalyzeAsync(assetSymbol);

        Assert.IsNotNull(report);
        Assert.IsTrue(progressEvents.Count > 0, "应该触发进度事件");
        Assert.IsTrue(progressEvents.Count >= 3, "至少应有分派、聚合、协调三个阶段的进度事件");

        foreach (var evt in progressEvents)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(evt), "阶段描述不应为空");
        }
    }

    [TestMethod]
    [TestCategory("E2E")]
    public async Task AnalyzeAsync_ReportShouldHaveQualityMetrics()
    {
        string assetSymbol = "000001";

        var report = await _workflow.AnalyzeAsync(assetSymbol);

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.CoordinatorResult);

        Assert.IsTrue(report.CoordinatorResult.RiskFactors.Count > 0, "完整报告应包含风险评估");

        Assert.IsTrue(report.CoordinatorResult.KeyIndicators.Count > 0, "完整报告应包含关键指标");

        Assert.IsTrue(report.AnalystMessages.Count >= 2,
            "应至少包含财务分析师和新闻事件分析师两位的结果（基于默认启用配置）");
    }
}
