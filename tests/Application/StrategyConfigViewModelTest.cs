using System.Text.Json;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class StrategyConfigViewModelTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task CreateDcaStrategy_InvalidStopLoss_ShouldRejectInsteadOfDisablingGuardrail()
    {
        var (viewModel, strategyService) = CreateViewModel();
        using var disposable = viewModel;
        viewModel.SelectedScenario = ScenarioKind.DCA;
        viewModel.NewSymbol = "BTCUSDT";
        viewModel.DcaAmountPerInterval = "100";
        viewModel.DcaStopLossPercent = "not-a-number";

        await viewModel.CreateStrategyCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.ValidationError, "非空数值参数");
        strategyService.Verify(s => s.SaveStrategyAsync(
                It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CreateDcaStrategy_BlankOptionalFields_ShouldUseDefaultsAndSave()
    {
        var (viewModel, strategyService) = CreateViewModel();
        using var disposable = viewModel;
        TradingStrategy? saved = null;
        strategyService.Setup(s => s.SaveStrategyAsync(It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()))
            .Callback<TradingStrategy, CancellationToken>((strategy, _) => saved = strategy)
            .Returns(Task.CompletedTask);
        viewModel.SelectedScenario = ScenarioKind.DCA;
        viewModel.NewSymbol = "BTCUSDT";
        viewModel.DcaAmountPerInterval = "100";
        viewModel.DcaIntervalSeconds = " ";
        viewModel.DcaMaxBuyPrice = string.Empty;
        viewModel.DcaDoubleBuyBelowPrice = string.Empty;
        viewModel.DcaTakeProfitPercent = string.Empty;
        viewModel.DcaStopLossPercent = string.Empty;

        await viewModel.CreateStrategyCommand.ExecuteAsync(null);

        Assert.IsNotNull(saved, viewModel.ValidationError);
        var parameters = JsonSerializer.Deserialize<DCAParams>(saved!.CustomParams!);
        Assert.IsNotNull(parameters);
        Assert.AreEqual(86400, parameters!.IntervalSeconds);
        Assert.AreEqual(0m, parameters.MaxBuyPrice);
        Assert.AreEqual(0m, parameters.DoubleBuyBelowPrice);
        Assert.AreEqual(0m, parameters.TakeProfitPercent);
        Assert.AreEqual(0m, parameters.StopLossPercent);
        strategyService.Verify(s => s.SaveStrategyAsync(
                It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CreateGridStrategy_InvalidGuardrail_ShouldRejectWithoutSaving()
    {
        var (viewModel, strategyService) = CreateViewModel();
        using var disposable = viewModel;
        viewModel.SelectedScenario = ScenarioKind.Grid;
        viewModel.NewSymbol = "BTCUSDT";
        viewModel.GridUpperPrice = "120";
        viewModel.GridLowerPrice = "100";
        viewModel.GridCount = "10";
        viewModel.GridQuantityPerGrid = "1";
        viewModel.GridStopLossPrice = "invalid";

        await viewModel.CreateStrategyCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.ValidationError, "非空数值参数");
        strategyService.Verify(s => s.SaveStrategyAsync(
                It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (StrategyConfigViewModel ViewModel, Mock<TradingStrategyService> StrategyService) CreateViewModel()
    {
        var dataService = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        dataService.Setup(s => s.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig { MinOrderAmount = 0 });
        dataService.Setup(s => s.GetAllStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var strategyService = new Mock<TradingStrategyService>(null!);
        var monitor = new Mock<MarketMonitor>();
        monitor.SetupGet(m => m.IsRunning).Returns(false);
        var portfolioService = new Mock<CryptoPortfolioService>(
            null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
        var viewModel = new StrategyConfigViewModel(
            strategyService.Object,
            dataService.Object,
            monitor.Object,
            Mock.Of<IDialogService>(),
            portfolioService.Object,
            Mock.Of<INotificationService>(),
            NullLogger<StrategyConfigViewModel>.Instance);
        return (viewModel, strategyService);
    }
}
