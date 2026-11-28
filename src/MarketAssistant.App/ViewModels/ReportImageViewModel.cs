using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Extensions.Logging.Abstractions;
using AdaptiveCardConverter = MarketAssistant.Infrastructure.AdaptiveCards.AdaptiveCardConverter;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 报告长图的展示数据：与页面同一份 <see cref="AnalysisReportViewModel"/>，外加页面不展示、导出才补全的附录
/// （协调分析师提炼的关键指标、各专业分析师的详细结论）。
/// </summary>
public sealed record ReportImageViewModel(
    AnalysisReportViewModel Report,
    IReadOnlyList<KeyIndicator> KeyIndicators,
    IReadOnlyList<ChatMessageAdapter> AnalystMessages)
{
    public bool HasKeyIndicators => KeyIndicators.Count > 0;

    public bool HasAnalystMessages => AnalystMessages.Count > 0;

    /// <summary>
    /// 由报告现建展示数据，与页面当前绑定的实例解耦，导出内容只取决于传入的报告。
    /// 分析师详情复用聊天侧栏的 <see cref="ChatMessageAdapter"/>，同一份结论在导出里与侧栏里是同一张卡片；
    /// 协调分析师的结论就是报告正文本身，不再作为分析师详情重复出现。
    /// </summary>
    public static ReportImageViewModel Create(MarketAnalysisReport report, string assetName, AdaptiveCardConverter converter)
    {
        var reportViewModel = new AnalysisReportViewModel(NullLogger<AnalysisReportViewModel>.Instance);
        reportViewModel.UpdateWithReport(report);
        reportViewModel.AssetName = assetName;

        var analystMessages = report.AnalystMessages
            .Where(message => message.AuthorName != AnalystNames.Coordinator && !string.IsNullOrWhiteSpace(message.Text))
            .Select(message => new ChatMessageAdapter(message, converter))
            .ToList();

        return new ReportImageViewModel(reportViewModel, report.CoordinatorResult.KeyIndicators, analystMessages);
    }
}
