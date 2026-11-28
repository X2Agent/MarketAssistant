using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.AdaptiveCards;
using MarketAssistant.Services;
using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketAssistant.ViewModels.Demo;

/// <summary>
/// 卡片预览页（仅 Debug 构建的导航里可见）：用真实的解析器链与聊天侧栏渲染示例分析师回复，
/// 调整卡片解析或样式后可在这里直接目测，无需真跑一轮分析。
/// </summary>
public partial class CardPreviewViewModel : ViewModelBase, IDisposable
{
    private static readonly string PreviewDatabasePath =
        Path.Combine(Path.GetTempPath(), "MarketAssistant", "card-preview.db");

    private readonly AdaptiveCardConverter _converter;

    public override string Title => "卡片预览";

    /// <summary>与线上同一套聊天侧栏；背后是假聊天客户端与隔离的临时库，不触碰真实模型与历史会话。</summary>
    public ChatSidebarViewModel Sidebar { get; }

    public IReadOnlyList<CardPreviewSample> Samples => CardPreviewSamples.All;

    public IReadOnlyList<CardPreviewShapeOption> ShapeOptions => CardPreviewSamples.ShapeOptions;

    [ObservableProperty]
    private CardPreviewShapeOption _selectedShapeOption = CardPreviewSamples.ShapeOptions[0];

    public CardPreviewViewModel(IDialogService dialogService, AdaptiveCardConverter converter)
    {
        _converter = converter;
        Sidebar = new ChatSidebarViewModel(
            NullLogger<ChatSidebarViewModel>.Instance,
            new PreviewChatSessionFactory(),
            new ChatSessionPersistenceService(NullLogger<ChatSessionPersistenceService>.Instance, PreviewDatabasePath),
            dialogService,
            converter);
        Sidebar.InitializeEmpty();
    }

    /// <summary>按当前选中的回复形态，把一个分析师样本追加到聊天里。</summary>
    [RelayCommand]
    private void AddSample(CardPreviewSample sample)
        => Sidebar.ChatMessages.Add(new ChatMessageAdapter(CreateMessage(sample, SelectedShapeOption.Shape), _converter));

    /// <summary>模拟打开一份历史分析报告：与线上「带着分析结果进入聊天」走同一个入口。</summary>
    [RelayCommand]
    private Task LoadFullAnalysisAsync()
    {
        var messages = CardPreviewSamples.FullAnalysis.Select(item => CreateMessage(item.Sample, item.Shape));
        return Sidebar.InitializeWithAnalysisHistory(CardPreviewSamples.StockCode, messages);
    }

    [RelayCommand]
    private void ClearMessages() => Sidebar.InitializeEmpty();

    public void Dispose() => Sidebar.Dispose();

    private static ChatMessage CreateMessage(CardPreviewSample sample, CardPreviewShape shape)
        => new(ChatRole.Assistant, CardPreviewSamples.Render(sample, shape)) { AuthorName = sample.AgentName };
}
