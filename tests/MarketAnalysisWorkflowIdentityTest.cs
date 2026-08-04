using System.Collections.Concurrent;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public sealed class MarketAnalysisWorkflowIdentityTest
{
    [TestMethod]
    public async Task AnalyzeAsync_ConcurrentFailedRuns_ShouldKeepRunAndAssetIdentityIsolated()
    {
        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(service => service.CurrentSetting).Returns(new UserSetting
        {
            EnabledAnalystRoles = new Dictionary<string, bool>()
        });
        var workflow = new MarketAnalysisWorkflow(
            settingService.Object,
            Mock.Of<IAnalystAgentFactory>(),
            Mock.Of<IChatClientFactory>(),
            NullLoggerFactory.Instance,
            new AnalysisReportCache(new MarketContext(settingService.Object, Mock.Of<IServiceProvider>())),
            NullLogger<MarketAnalysisWorkflow>.Instance);
        var runA = Guid.NewGuid();
        var runB = Guid.NewGuid();
        var events = new ConcurrentBag<AnalysisProgressEventArgs>();
        workflow.ProgressChanged += (_, args) => events.Add(args);

        var first = RecordExceptionAsync(() => workflow.AnalyzeAsync("000001", runA));
        var second = RecordExceptionAsync(() => workflow.AnalyzeAsync("BTCUSDT", runB));

        var exceptions = await Task.WhenAll(first, second);
        Assert.IsTrue(exceptions.All(exception => exception is not null));

        var eventsA = events.Where(item => item.RunId == runA).ToList();
        var eventsB = events.Where(item => item.RunId == runB).ToList();

        Assert.HasCount(2, eventsA);
        Assert.HasCount(2, eventsB);
        Assert.IsTrue(eventsA.All(item => item.AssetSymbol == "000001"));
        Assert.IsTrue(eventsB.All(item => item.AssetSymbol == "BTCUSDT"));
        Assert.IsTrue(eventsA.Any(item => !item.IsInProgress));
        Assert.IsTrue(eventsB.Any(item => !item.IsInProgress));
        Assert.IsFalse(events.Any(item =>
            item.RunId == runA && item.AssetSymbol != "000001" ||
            item.RunId == runB && item.AssetSymbol != "BTCUSDT"));
    }

    [TestMethod]
    public async Task AnalyzeAsync_EmptyRunId_ShouldFailBeforePublishingProgress()
    {
        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(service => service.CurrentSetting).Returns(new UserSetting());
        var workflow = new MarketAnalysisWorkflow(
            settingService.Object,
            Mock.Of<IAnalystAgentFactory>(),
            Mock.Of<IChatClientFactory>(),
            NullLoggerFactory.Instance,
            new AnalysisReportCache(new MarketContext(settingService.Object, Mock.Of<IServiceProvider>())),
            NullLogger<MarketAnalysisWorkflow>.Instance);
        var progressCount = 0;
        workflow.ProgressChanged += (_, _) => progressCount++;

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => workflow.AnalyzeAsync("000001", Guid.Empty));

        Assert.AreEqual(0, progressCount);
    }

    private static async Task<Exception?> RecordExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
