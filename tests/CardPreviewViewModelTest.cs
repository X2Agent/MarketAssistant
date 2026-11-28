using MarketAssistant.Services.Dialog;
using MarketAssistant.ViewModels.Demo;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 卡片预览页的命令应落在真实的聊天侧栏与卡片渲染链路上：样本经解析器链渲染成卡片，而不是原文 JSON。
/// </summary>
[TestClass]
public sealed class CardPreviewViewModelTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadFullAnalysisCommand_ShouldRenderEveryAnalystReplyAsCard()
    {
        using var viewModel = CreateViewModel();

        await viewModel.LoadFullAnalysisCommand.ExecuteAsync(null);

        var cardCount = viewModel.Sidebar.ChatMessages.Count(message => message.IsAdaptiveCard);
        Assert.AreEqual(CardPreviewSamples.FullAnalysis.Count, cardCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AddSampleCommand_ShouldAppendSampleAsCard_ForEverySample()
    {
        using var viewModel = CreateViewModel();

        foreach (var sample in CardPreviewSamples.All)
        {
            viewModel.AddSampleCommand.Execute(sample);

            Assert.IsTrue(viewModel.Sidebar.ChatMessages[^1].IsAdaptiveCard, $"追加「{sample.Label}」后最后一条不是卡片");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ClearMessagesCommand_ShouldRemoveRenderedCards()
    {
        using var viewModel = CreateViewModel();
        await viewModel.LoadFullAnalysisCommand.ExecuteAsync(null);

        viewModel.ClearMessagesCommand.Execute(null);

        Assert.IsFalse(viewModel.Sidebar.ChatMessages.Any(message => message.IsAdaptiveCard));
    }

    private static CardPreviewViewModel CreateViewModel()
        => new(new Mock<IDialogService>().Object, AdaptiveCardConverterTest.CreateConverter());
}
