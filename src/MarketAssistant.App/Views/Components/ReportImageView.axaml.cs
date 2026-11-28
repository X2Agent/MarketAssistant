using Avalonia.Controls;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 报告长图（离屏渲染用）：页面同一个 <see cref="AnalysisReportView"/>，加页面没有的附录
/// （关键指标、各分析师详细分析）与页脚声明；仅由 <c>ReportImageRenderer</c> 使用。
/// </summary>
public partial class ReportImageView : UserControl
{
    public ReportImageView()
    {
        InitializeComponent();
    }
}
