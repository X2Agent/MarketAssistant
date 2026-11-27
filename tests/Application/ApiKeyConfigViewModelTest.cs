using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class ApiKeyConfigViewModelTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task Clear_UserCancels_ShouldKeepCredentials()
    {
        var (viewModel, credentialStore, _) = CreateViewModel(TradingMode.LiveSpot, confirmed: false);

        await viewModel.ClearCommand.ExecuteAsync(null);

        credentialStore.Verify(s => s.ClearCredentials(It.IsAny<TradingMode>()), Times.Never);
        Assert.AreEqual("api-key", viewModel.ApiKey);
        Assert.AreEqual("secret-key", viewModel.SecretKey);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Clear_LiveMode_UserConfirms_ShouldClearOnlyTheSharedLivePair()
    {
        var (viewModel, credentialStore, dialog) = CreateViewModel(TradingMode.LiveFutures, confirmed: true);

        await viewModel.ClearCommand.ExecuteAsync(null);

        credentialStore.Verify(s => s.ClearCredentials(TradingMode.LiveFutures), Times.Once);
        credentialStore.Verify(s => s.ClearCredentials(TradingMode.LiveSpot), Times.Once);
        credentialStore.Verify(s => s.ClearCredentials(TradingMode.SpotDemo), Times.Never);
        credentialStore.Verify(s => s.ClearCredentials(TradingMode.FuturesTestnet), Times.Never);
        Assert.AreEqual(string.Empty, viewModel.ApiKey);
        Assert.AreEqual(string.Empty, viewModel.SecretKey);
        VerifyDestructiveConfirmation(dialog, "实盘（现货 + 合约共享）");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Clear_DemoMode_UserConfirms_ShouldClearOnlyTheSharedDemoPair()
    {
        var (viewModel, credentialStore, dialog) = CreateViewModel(TradingMode.SpotDemo, confirmed: true);

        await viewModel.ClearCommand.ExecuteAsync(null);

        credentialStore.Verify(s => s.ClearCredentials(TradingMode.SpotDemo), Times.Once);
        credentialStore.Verify(s => s.ClearCredentials(TradingMode.FuturesTestnet), Times.Once);
        credentialStore.Verify(s => s.ClearCredentials(TradingMode.LiveSpot), Times.Never);
        credentialStore.Verify(s => s.ClearCredentials(TradingMode.LiveFutures), Times.Never);
        VerifyDestructiveConfirmation(dialog, "Demo（现货 Demo + Futures Testnet 共享）");
    }

    private static void VerifyDestructiveConfirmation(Mock<IDialogService> dialog, string expectedGroupName)
    {
        dialog.Verify(d => d.ShowConfirmationAsync(
                "清除 API 密钥",
                It.Is<string>(message => message.Contains(expectedGroupName)),
                "清除",
                "取消",
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>(),
                true),
            Times.Once);
    }

    private static (ApiKeyConfigViewModel ViewModel, Mock<ITradingCredentialStore> CredentialStore, Mock<IDialogService> Dialog)
        CreateViewModel(TradingMode currentMode, bool confirmed)
    {
        var credentialStore = new Mock<ITradingCredentialStore>();
        credentialStore.Setup(s => s.GetCredentials(It.IsAny<TradingMode>())).Returns(("api-key", "secret-key"));

        var settings = new Mock<IUserSettingService>();
        settings.SetupGet(s => s.CurrentSetting).Returns(new UserSetting { CryptoTradingMode = currentMode });
        var environment = new TradingEnvironmentService(
            settings.Object, Mock.Of<IMarketMonitorProvider>(), NullLogger<TradingEnvironmentService>.Instance);

        var dialog = new Mock<IDialogService>();
        dialog.Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(confirmed);

        var viewModel = new ApiKeyConfigViewModel(
            credentialStore.Object,
            environment,
            Mock.Of<INotificationService>(),
            new Mock<MarketMonitor>().Object,
            dialog.Object,
            NullLogger<ApiKeyConfigViewModel>.Instance);
        return (viewModel, credentialStore, dialog);
    }
}
