using Avalonia.Controls;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 报告分享卡（离屏渲染用）。画布固定 1080×1350，仅由 <c>ReportShareCardRenderer</c> 使用。
/// </summary>
public partial class ReportShareCardView : UserControl
{
    public ReportShareCardView()
    {
        InitializeComponent();
    }

    public ReportShareCardView(ReportShareCardData data) : this()
    {
        DataContext = data;
    }
}
