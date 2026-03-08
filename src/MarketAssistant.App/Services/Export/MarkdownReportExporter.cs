using System.Text;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Extensions;

namespace MarketAssistant.Services.Export;

/// <summary>
/// 将市场分析报告导出为 Markdown 格式
/// </summary>
public static class MarkdownReportExporter
{
    /// <summary>
    /// 将 MarketAnalysisReport 转换为 Markdown 文本
    /// </summary>
    public static string Export(MarketAnalysisReport report)
    {
        var cr = report.CoordinatorResult;
        var sb = new StringBuilder();

        sb.AppendLine($"# {report.StockSymbol} 投资分析报告");
        sb.AppendLine();
        sb.AppendLine($"> 生成时间：{report.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        sb.AppendLine("## 综合评级");
        sb.AppendLine();
        sb.AppendLine($"| 指标 | 值 |");
        sb.AppendLine($"|------|------|");
        sb.AppendLine($"| 综合评分 | **{cr.OverallScore:F1}** / 10 |");
        sb.AppendLine($"| 投资评级 | {cr.InvestmentRating.GetDescription()} |");
        sb.AppendLine($"| 目标价格 | {cr.TargetPrice} |");
        sb.AppendLine($"| 价格预期 | {cr.PriceChangeExpectation} |");
        sb.AppendLine($"| 投资周期 | {cr.TimeHorizon.GetDescription()} {cr.TimeHorizonDescription} |");
        sb.AppendLine($"| 风险等级 | {cr.RiskLevel.GetDescription()} |");
        sb.AppendLine($"| 置信度 | {cr.ConfidencePercentage:F0}% |");
        sb.AppendLine();

        sb.AppendLine("## 各维度评分");
        sb.AppendLine();
        var ds = cr.DimensionScores;
        sb.AppendLine($"| 维度 | 评分 |");
        sb.AppendLine($"|------|------|");
        sb.AppendLine($"| 基本面 | {ds.Fundamental:F1} |");
        sb.AppendLine($"| 技术面 | {ds.Technical:F1} |");
        sb.AppendLine($"| 财务面 | {ds.Financial:F1} |");
        sb.AppendLine($"| 市场情绪 | {ds.Sentiment:F1} |");
        sb.AppendLine($"| 新闻事件 | {ds.News:F1} |");
        sb.AppendLine();

        AppendListSection(sb, "操作建议", cr.OperationSuggestions);
        AppendListSection(sb, "投资亮点", cr.InvestmentHighlights);
        AppendListSection(sb, "风险因素", cr.RiskFactors);

        if (!string.IsNullOrWhiteSpace(cr.ConsensusAnalysis))
        {
            sb.AppendLine("## 核心共识");
            sb.AppendLine();
            sb.AppendLine(cr.ConsensusAnalysis);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(cr.DisagreementAnalysis))
        {
            sb.AppendLine("## 主要分歧");
            sb.AppendLine();
            sb.AppendLine(cr.DisagreementAnalysis);
            sb.AppendLine();
        }

        if (cr.KeyIndicators is { Count: > 0 })
        {
            sb.AppendLine("## 关键指标");
            sb.AppendLine();
            sb.AppendLine("| 来源 | 类别 | 指标 | 值 | 信号 | 建议 |");
            sb.AppendLine("|------|------|------|------|------|------|");
            foreach (var ki in cr.KeyIndicators)
            {
                sb.AppendLine($"| {ki.AnalystSource} | {ki.Category} | {ki.Name} | {ki.Value} | {ki.Signal} | {ki.Suggestion} |");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(cr.Summary))
        {
            sb.AppendLine("## 一句话总结");
            sb.AppendLine();
            sb.AppendLine($"**{cr.Summary}**");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("*本报告由 MarketAssistant AI 多 Agent 系统自动生成，仅供参考，不构成投资建议。*");

        return sb.ToString();
    }

    private static void AppendListSection(StringBuilder sb, string title, List<string>? items)
    {
        if (items is not { Count: > 0 }) return;

        sb.AppendLine($"## {title}");
        sb.AppendLine();
        foreach (var item in items)
        {
            sb.AppendLine($"- {item}");
        }
        sb.AppendLine();
    }
}
