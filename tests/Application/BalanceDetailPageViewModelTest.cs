using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class BalanceDetailPageViewModelTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task RefreshAsync_FirstLoadEmpty_ShouldExposeSkeletonThenEmptyState()
    {
        var completion = new TaskCompletionSource<AccountBalanceSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var portfolio = CreatePortfolio();
        portfolio.Setup(p => p.GetAccountBalanceSummaryAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(completion.Task);
        var viewModel = new BalanceDetailPageViewModel(portfolio.Object, NullLogger<BalanceDetailPageViewModel>.Instance);

        var commandTask = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.AreEqual(LoadState.Loading, viewModel.State);
        Assert.IsTrue(viewModel.IsInitialLoading);
        completion.SetResult(new AccountBalanceSummary());
        await commandTask;

        Assert.AreEqual(LoadState.Empty, viewModel.State);
        Assert.AreEqual(LoadState.Empty, viewModel.OverlayState);
        Assert.IsFalse(viewModel.IsInitialLoading);
        Assert.IsFalse(viewModel.HasData);
        Assert.IsNotNull(viewModel.RetryCommand);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RefreshAsync_FailureThenRetry_ShouldTransitionFromFailedToReady()
    {
        var portfolio = CreatePortfolio();
        portfolio.Setup(p => p.GetAccountBalanceSummaryAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new HttpRequestException("network down"));
        var viewModel = new BalanceDetailPageViewModel(portfolio.Object, NullLogger<BalanceDetailPageViewModel>.Instance);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.AreEqual(LoadState.Failed, viewModel.State);
        Assert.AreEqual(LoadState.Failed, viewModel.OverlayState);
        Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
        portfolio.Setup(p => p.GetAccountBalanceSummaryAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new AccountBalanceSummary
            {
                TotalValueUSDT = 100m,
                Assets = [new AssetBalance { Asset = "USDT", Free = 100m, ValueUSDT = 100m }]
            });

        await viewModel.RetryCommand!.ExecuteAsync(null);

        Assert.AreEqual(LoadState.Ready, viewModel.State);
        Assert.IsTrue(viewModel.HasData);
        Assert.HasCount(1, viewModel.Balances);
        Assert.AreEqual(100m, viewModel.TotalValueUSDT);
    }

    private static Mock<CryptoPortfolioService> CreatePortfolio() => new(
        null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
}
