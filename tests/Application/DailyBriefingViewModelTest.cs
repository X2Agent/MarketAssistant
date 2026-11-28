using MarketAssistant.Applications.Briefing;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Home;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class DailyBriefingViewModelTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void LoadCached_CacheMissing_ShouldShowEmptyWithoutGenerating()
    {
        var briefingService = CreateBriefingService();
        briefingService.Setup(s => s.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyBriefing?)null);
        using var viewModel = CreateViewModel(briefingService);

        Assert.AreEqual(LoadState.Empty, viewModel.State);
        Assert.IsFalse(viewModel.HasBriefing);
        briefingService.Verify(s => s.GenerateAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadCached_FailureThenRetry_ShouldOnlyReloadCacheWithoutGenerating()
    {
        var briefingService = CreateBriefingService();
        briefingService.Setup(s => s.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache damaged"));
        using var viewModel = CreateViewModel(briefingService);

        Assert.AreEqual(LoadState.Failed, viewModel.State);
        Assert.IsNotNull(viewModel.RetryCommand);
        briefingService.Setup(s => s.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyBriefing
            {
                MarketType = MarketType.Crypto,
                DateKey = "2026-11-18",
                Content = "今日简报",
                GeneratedAt = DateTime.UtcNow
            });

        await viewModel.RetryCommand!.ExecuteAsync(null);

        Assert.AreEqual(LoadState.Ready, viewModel.State);
        Assert.IsTrue(viewModel.HasBriefing);
        Assert.AreEqual("今日简报", viewModel.BriefingContent);
        briefingService.Verify(s => s.GenerateAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IDailyBriefingService> CreateBriefingService()
    {
        var service = new Mock<IDailyBriefingService>();
        service.SetupGet(s => s.IsEnabled).Returns(true);
        service.Setup(s => s.GetUsageSummaryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyBriefingUsageSummary(0, 0, 0, false));
        return service;
    }

    private static DailyBriefingViewModel CreateViewModel(Mock<IDailyBriefingService> briefingService)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(MarketType.Crypto, briefingService.Object);
        var provider = services.BuildServiceProvider();
        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(s => s.CurrentSetting)
            .Returns(new UserSetting { CurrentMarketType = MarketType.Crypto });
        var marketContext = new MarketContext(settingService.Object, provider);
        return new DailyBriefingViewModel(
            marketContext,
            Mock.Of<IWindowService>(),
            NullLogger<DailyBriefingViewModel>.Instance);
    }
}
