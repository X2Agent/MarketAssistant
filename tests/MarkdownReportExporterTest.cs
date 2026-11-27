using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Services.Export;
using Microsoft.Extensions.AI;

namespace TestMarketAssistant;

[TestClass]
public sealed class MarkdownReportExporterTest
{
    private static readonly string NewLine = Environment.NewLine;

    private static MarketAnalysisReport CreateReport(params ChatMessage[] analystMessages) => new()
    {
        AssetSymbol = "sz300000",
        AnalystMessages = [.. analystMessages],
        CoordinatorResult = new CoordinatorResult { OverallScore = 7.8f },
        CreatedAt = new DateTime(2026, 11, 27, 4, 0, 0, DateTimeKind.Utc)
    };

    private static ChatMessage Analyst(string text)
        => new(ChatRole.Assistant, text) { AuthorName = AnalystNames.Fundamental };

    [TestMethod]
    [TestCategory("Unit")]
    public void Export_ShouldRenderSummaryHeadingAndScore()
    {
        var markdown = MarkdownReportExporter.Export(CreateReport());

        Assert.Contains("# sz300000 投资分析报告", markdown);
        Assert.Contains("| 综合评分 | **7.8** / 10 |", markdown);
        Assert.DoesNotContain("## 各分析师详细分析", markdown);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Export_AnalystHeading_ShouldUseChineseDisplayName_AndKeepUnknownNames()
    {
        var markdown = MarkdownReportExporter.Export(CreateReport(
            Analyst("基本面结论"),
            new ChatMessage(ChatRole.Assistant, "其他结论") { AuthorName = "UnknownAgent" }));

        Assert.Contains("### 基本面分析师", markdown);
        Assert.DoesNotContain("### FundamentalAnalyst", markdown);
        Assert.Contains("### UnknownAgent", markdown);
    }

    /// <summary>JSON 里的下划线、星号、反斜杠、引号、反引号不能被行内转义改写，否则导出的 JSON 不再可用。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Export_AnalystJson_ShouldBeEmittedVerbatimInJsonFence()
    {
        const string json = """{"snake_key":"a*b","path":"C:\\dir","quote":"say \"hi\"","code":"`x`"}""";

        var markdown = MarkdownReportExporter.Export(CreateReport(Analyst(json)));

        Assert.Contains($"```json{NewLine}{json}{NewLine}```", markdown);
    }

    /// <summary>
    /// 与聊天卡片、信号解析一致：只认首个结果 JSON，前后说明、围栏和模型重复输出的第二份都不进导出。
    /// 此前导出器单独把围栏外的剩余文本原样输出，同一份分析在卡片里正常、导出里却出现重复 JSON。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Export_PrefaceFenceAndRepeatedJson_ShouldKeepOnlyFirstResultJson()
    {
        const string json = """{"score":8}""";
        var text = $"说明_文字{NewLine}{NewLine}```json{NewLine}{json}{NewLine}```{NewLine}{json}";

        var markdown = MarkdownReportExporter.Export(CreateReport(Analyst(text)));

        Assert.Contains($"```json{NewLine}{json}{NewLine}```", markdown);
        Assert.AreEqual(1, CountOccurrences(markdown, json));
        Assert.AreEqual(1, CountOccurrences(markdown, "```json"));
        Assert.DoesNotContain("说明", markdown);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Export_MessageWithoutJson_ShouldStayInlineEscaped()
    {
        var markdown = MarkdownReportExporter.Export(CreateReport(Analyst("分析失败: 工具_调用 超时")));

        Assert.Contains("分析失败: 工具\\_调用 超时", markdown);
        Assert.DoesNotContain("```json", markdown);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
