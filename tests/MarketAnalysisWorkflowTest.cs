using MarketAssistant.Agents.MarketAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// MarketAnalysisWorkflow 测试（基于 Agent Framework Workflows 并发工作流）
/// </summary>
[TestClass]
public sealed class MarketAnalysisWorkflowTest : BaseKernelTest
{
    private MarketAnalysisWorkflow _workflow = null!;

    [TestInitialize]
    public void Initialize()
    {
        _workflow = _kernel.Services.GetRequiredService<MarketAnalysisWorkflow>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _workflow?.Dispose();
    }

    [TestMethod]
    public async Task TestMarketAnalysisWorkflow_AnalyzeAsync_WithValidStockSymbol_ShouldReturnReport()
    {
        // Arrange
        string stockSymbol = "000001";

        // Act
        var report = await _workflow.AnalyzeAsync(stockSymbol);

        // Assert
        Assert.IsNotNull(report);
        Assert.AreEqual(stockSymbol, report.StockSymbol);
        Assert.IsNotNull(report.AnalystResults);
        Assert.IsTrue(report.AnalystResults.Count > 0, "应该至少有一位分析师的结果");
        Assert.IsFalse(string.IsNullOrWhiteSpace(report.CoordinatorSummary), "协调分析师应该生成总结报告");
        Assert.IsNotNull(report.ChatHistory);

        Console.WriteLine($"=== 市场分析工作流测试结果 ===");
        Console.WriteLine($"股票代码: {report.StockSymbol}");
        Console.WriteLine($"分析师数量: {report.AnalystResults.Count}");
        Console.WriteLine($"协调总结长度: {report.CoordinatorSummary.Length} 字符");
        Console.WriteLine($"对话历史条数: {report.ChatHistory.Count}");

        Console.WriteLine($"\n各分析师结果:");
        foreach (var result in report.AnalystResults)
        {
            Console.WriteLine($"  - {result.AnalystName}: {result.Content.Substring(0, Math.Min(50, result.Content.Length))}...");
        }
    }

    [TestMethod]
    public async Task TestMarketAnalysisWorkflow_AnalyzeAsync_WithAnotherStock_ShouldReturnReport()
    {
        // Arrange
        string stockSymbol = "600519";

        // Act
        var report = await _workflow.AnalyzeAsync(stockSymbol);

        // Assert
        Assert.IsNotNull(report);
        Assert.AreEqual(stockSymbol, report.StockSymbol);
        Assert.IsTrue(report.AnalystResults.Count > 0);

        Console.WriteLine($"=== 不同股票分析测试结果 ===");
        Console.WriteLine($"股票代码: {report.StockSymbol}");
        Console.WriteLine($"分析师数量: {report.AnalystResults.Count}");
    }

    [TestMethod]
    public async Task TestMarketAnalysisWorkflow_AnalyzeAsync_ProgressEvents_ShouldBeTriggered()
    {
        // Arrange
        string stockSymbol = "000001";
        var progressEvents = new List<string>();

        _workflow.ProgressChanged += (sender, e) =>
        {
            progressEvents.Add($"{e.CurrentAnalyst}: {e.StageDescription}");
        };

        // Act
        var report = await _workflow.AnalyzeAsync(stockSymbol);

        // Assert
        Assert.IsNotNull(report);
        Assert.IsTrue(progressEvents.Count > 0, "应该触发进度事件");

        Console.WriteLine($"=== 进度事件测试结果 ===");
        Console.WriteLine($"触发的进度事件数: {progressEvents.Count}");
        foreach (var evt in progressEvents)
        {
            Console.WriteLine($"  - {evt}");
        }
    }

    [TestMethod]
    public async Task TestMarketAnalysisWorkflow_ConcurrentAnalysis_ShouldCompleteWithinReasonableTime()
    {
        // Arrange
        string stockSymbol = "000001";
        var startTime = DateTime.UtcNow;

        // Act
        var report = await _workflow.AnalyzeAsync(stockSymbol);

        var elapsedTime = DateTime.UtcNow - startTime;

        // Assert
        Assert.IsNotNull(report);
        Assert.IsTrue(elapsedTime.TotalMinutes < 5, "并发分析应该在5分钟内完成");

        Console.WriteLine($"=== 并发性能测试结果 ===");
        Console.WriteLine($"执行时间: {elapsedTime.TotalSeconds:F2} 秒");
        Console.WriteLine($"分析师数量: {report.AnalystResults.Count}");
        Console.WriteLine($"平均每位分析师: {elapsedTime.TotalSeconds / report.AnalystResults.Count:F2} 秒");
    }
}


