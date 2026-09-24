using System.Text.Json;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.TokenManagement;

namespace TestMarketAssistant.Middleware;

/// <summary>
/// <see cref="AnalysisTokenUsage"/> 作为持久化模型（随 <see cref="MarketAnalysisReport"/> 序列化到归档）
/// 的兼容性测试：<c>[JsonIgnore]</c> 派生属性不得进入 JSON，历史报告缺失 Usage 时读取方必须容忍。
/// </summary>
[TestClass]
public class TokenUsagePersistenceTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void AnalysisTokenUsage_Serialization_ShouldOmitComputedProperties()
    {
        var usage = new AnalysisTokenUsage
        {
            InputTokens = 100,
            OutputTokens = 40,
            CallCount = 2,
            IsEstimated = true,
            Entries =
            [
                new TokenUsageEntry
                {
                    AgentName = "基本面分析师",
                    InputTokens = 100,
                    OutputTokens = 40,
                    CallCount = 2,
                    IsEstimated = true
                }
            ]
        };

        var json = JsonSerializer.Serialize(usage);

        Assert.IsFalse(json.Contains("TotalTokens", StringComparison.Ordinal), "派生属性不应进入持久化 JSON");
        Assert.IsFalse(json.Contains("HasValue", StringComparison.Ordinal), "派生属性不应进入持久化 JSON");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Report_WithUsage_ShouldRoundTripThroughJson()
    {
        var report = new MarketAnalysisReport
        {
            AssetSymbol = "SH600519",
            Usage = new AnalysisTokenUsage
            {
                InputTokens = 1_500,
                OutputTokens = 600,
                CallCount = 3,
                Entries =
                [
                    new TokenUsageEntry { AgentName = "基本面分析师", InputTokens = 1_000, OutputTokens = 400, CallCount = 2 },
                    new TokenUsageEntry { AgentName = "技术面分析师", InputTokens = 500, OutputTokens = 200, CallCount = 1, IsEstimated = true }
                ]
            }
        };

        var json = JsonSerializer.Serialize(report);
        var restored = JsonSerializer.Deserialize<MarketAnalysisReport>(json);

        Assert.IsNotNull(restored);
        Assert.IsNotNull(restored.Usage);
        Assert.AreEqual(2_100L, restored.Usage.TotalTokens);
        Assert.AreEqual(3, restored.Usage.CallCount);
        Assert.HasCount(2, restored.Usage.Entries);

        var fundamental = restored.Usage.EntryFor("基本面分析师");
        Assert.IsNotNull(fundamental);
        Assert.AreEqual(1_000L, fundamental.InputTokens);
        Assert.AreEqual(400L, fundamental.OutputTokens);
        Assert.IsFalse(fundamental.IsEstimated);

        var technical = restored.Usage.EntryFor("技术面分析师");
        Assert.IsNotNull(technical);
        Assert.IsTrue(technical.IsEstimated);
        Assert.AreEqual(700L, technical.TotalTokens);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Deserialize_LegacyReportWithoutUsage_ShouldYieldNullUsageWithoutThrowing()
    {
        // 早期归档的 JSON 没有 Usage 字段
        const string legacyJson = """{"AssetSymbol":"SH600519","AnalystMessages":[]}""";

        var restored = JsonSerializer.Deserialize<MarketAnalysisReport>(legacyJson);

        Assert.IsNotNull(restored);
        Assert.AreEqual("SH600519", restored.AssetSymbol);
        Assert.IsNull(restored.Usage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Deserialize_ReportWithNullUsage_ShouldYieldNullUsageWithoutThrowing()
    {
        const string json = """{"AssetSymbol":"SH600519","Usage":null}""";

        var restored = JsonSerializer.Deserialize<MarketAnalysisReport>(json);

        Assert.IsNotNull(restored);
        Assert.IsNull(restored.Usage);
    }
}
