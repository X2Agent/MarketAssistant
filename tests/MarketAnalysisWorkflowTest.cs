using MarketAssistant.Agents.MarketAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// 市场分析工作流测试（最小原则：验证核心工作流功能）
/// </summary>
[TestClass]
public sealed class MarketAnalysisWorkflowTest : BaseAgentTest
{
    private MarketAnalysisWorkflow _workflow = null!;

    [TestInitialize]
    public void Initialize()
    {
        _workflow = _serviceProvider.GetRequiredService<MarketAnalysisWorkflow>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _workflow?.Dispose();
    }

    [TestMethod]
    public async Task AnalyzeAsync_ShouldReturnValidReport()
    {
        // Arrange
        string stockSymbol = "000001";

        // Act
        var report = await _workflow.AnalyzeAsync(stockSymbol);

        // Assert
        Assert.IsNotNull(report);
        Assert.AreEqual(stockSymbol, report.StockSymbol);
        Assert.IsNotNull(report.AnalystMessages);
        Assert.IsTrue(report.AnalystMessages.Count > 0, "应该至少有一位分析师的结果");
        Assert.IsNotNull(report.CoordinatorResult, "协调分析师应该生成结果");
        Assert.IsFalse(string.IsNullOrWhiteSpace(report.CoordinatorResult.Summary), "协调分析师应该生成总结报告");

        Console.WriteLine($"股票 {stockSymbol} 分析完成 - 分析师数量: {report.AnalystMessages.Count}, 总结长度: {report.CoordinatorResult.Summary.Length} 字符");
    }

    [TestMethod]
    public async Task AnalyzeAsync_ShouldTriggerProgressEvents()
    {
        // Arrange
        string stockSymbol = "000001";
        var progressEvents = new List<string>();

        _workflow.ProgressChanged += (sender, e) =>
        {
            progressEvents.Add(e.StageDescription);
        };

        // Act
        var report = await _workflow.AnalyzeAsync(stockSymbol);

        // Assert
        Assert.IsNotNull(report);
        Assert.IsTrue(progressEvents.Count > 0, "应该触发进度事件");

        // 验证进度事件内容合理性
        foreach (var evt in progressEvents)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(evt), "阶段描述不应为空");
        }

        Console.WriteLine($"进度事件触发 {progressEvents.Count} 次 - 所有事件内容有效");
    }
}
