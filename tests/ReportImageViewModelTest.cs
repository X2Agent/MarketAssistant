using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Demo;
using Microsoft.Extensions.AI;

namespace TestMarketAssistant;

/// <summary>报告长图的附录数据：关键指标原样带出，分析师详情复用聊天卡片，协调分析师不在详情里重复出现。</summary>
[TestClass]
public sealed class ReportImageViewModelTest
{
    private static ChatMessage SampleMessage(string agentName)
    {
        var sample = CardPreviewSamples.All.First(candidate => candidate.AgentName == agentName);
        return new ChatMessage(ChatRole.Assistant, CardPreviewSamples.Render(sample, CardPreviewShape.PrefaceAndJson))
        {
            AuthorName = agentName
        };
    }

    private static MarketAnalysisReport CreateReport(List<KeyIndicator> keyIndicators, params ChatMessage[] analystMessages) => new()
    {
        AssetSymbol = "sz300000",
        AnalystMessages = [.. analystMessages],
        CoordinatorResult = new CoordinatorResult { OverallScore = 7.8f, KeyIndicators = keyIndicators },
        CreatedAt = new DateTime(2026, 11, 27, 4, 0, 0, DateTimeKind.Utc)
    };

    [TestMethod]
    [TestCategory("Unit")]
    public void Create_ShouldKeepAnalystDetailsInOrder_AndDropCoordinatorAndEmptyMessages()
    {
        var report = CreateReport(
            [],
            SampleMessage(AnalystNames.Fundamental),
            new ChatMessage(ChatRole.Assistant, "  ") { AuthorName = AnalystNames.Technical },
            SampleMessage(AnalystNames.Sentiment),
            SampleMessage(AnalystNames.Coordinator));

        var viewModel = ReportImageViewModel.Create(report, "东山精密", AdaptiveCardConverterTest.CreateConverter());

        CollectionAssert.AreEqual(
            new[] { AnalystNames.Fundamental, AnalystNames.Sentiment },
            viewModel.AnalystMessages.Select(message => message.Sender).ToArray());
        Assert.IsTrue(viewModel.HasAnalystMessages);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Create_ShouldRenderAnalystDetailsAsTheSameCardsAsTheChatSidebar()
    {
        var report = CreateReport([], SampleMessage(AnalystNames.Fundamental), SampleMessage(AnalystNames.Sentiment));

        var viewModel = ReportImageViewModel.Create(report, "东山精密", AdaptiveCardConverterTest.CreateConverter());

        Assert.IsTrue(viewModel.AnalystMessages.All(message => message.IsAdaptiveCard), "分析师详情应渲染成卡片而不是原文");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Create_ShouldPassKeyIndicatorsThroughAndCarryAssetName()
    {
        var indicator = new KeyIndicator { AnalystSource = "财务分析师", Category = "财务数据", Name = "ROE", Value = "22.13%", Signal = "健康" };
        var report = CreateReport([indicator]);

        var viewModel = ReportImageViewModel.Create(report, "东山精密", AdaptiveCardConverterTest.CreateConverter());

        Assert.HasCount(1, viewModel.KeyIndicators);
        Assert.AreSame(indicator, viewModel.KeyIndicators[0]);
        Assert.IsTrue(viewModel.HasKeyIndicators);
        Assert.AreEqual("东山精密", viewModel.Report.AssetName);
        Assert.AreEqual(7.8f, viewModel.Report.OverallScore);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Create_WithoutAppendixData_ShouldHideBothAppendixSections()
    {
        var viewModel = ReportImageViewModel.Create(CreateReport([]), "东山精密", AdaptiveCardConverterTest.CreateConverter());

        Assert.IsFalse(viewModel.HasKeyIndicators);
        Assert.IsFalse(viewModel.HasAnalystMessages);
    }
}
