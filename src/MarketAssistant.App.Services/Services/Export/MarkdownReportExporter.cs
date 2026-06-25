using System.Text;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Extensions;
using Microsoft.Extensions.AI;

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

        sb.AppendLine($"# {EscapeInline(report.AssetSymbol)} 投资分析报告");
        sb.AppendLine();
        sb.AppendLine($"> 生成时间：{report.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        sb.AppendLine("## 综合评级");
        sb.AppendLine();
        sb.AppendLine($"| 指标 | 值 |");
        sb.AppendLine($"|------|------|");
        sb.AppendLine($"| 综合评分 | **{cr.OverallScore:F1}** / 10 |");
        sb.AppendLine($"| 投资评级 | {EscapeTableCell(cr.InvestmentRating.GetDescription())} |");
        sb.AppendLine($"| 目标价格 | {EscapeTableCell(cr.TargetPrice)} |");
        sb.AppendLine($"| 价格预期 | {EscapeTableCell(cr.PriceChangeExpectation)} |");
        sb.AppendLine($"| 投资周期 | {EscapeTableCell(cr.TimeHorizon.GetDescription() + " " + cr.TimeHorizonDescription)} |");
        sb.AppendLine($"| 风险等级 | {EscapeTableCell(cr.RiskLevel.GetDescription())} |");
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
            sb.AppendLine(EscapeInline(cr.ConsensusAnalysis));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(cr.DisagreementAnalysis))
        {
            sb.AppendLine("## 主要分歧");
            sb.AppendLine();
            sb.AppendLine(EscapeInline(cr.DisagreementAnalysis));
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
                sb.AppendLine($"| {EscapeTableCell(ki.AnalystSource)} | {EscapeTableCell(ki.Category)} | {EscapeTableCell(ki.Name)} | {EscapeTableCell(ki.Value)} | {EscapeTableCell(ki.Signal)} | {EscapeTableCell(ki.Suggestion)} |");
            }
            sb.AppendLine();
        }

        AppendQualityMetricsSection(sb, cr.QualityMetrics);
        AppendAnalystMessagesSection(sb, report.AnalystMessages);

        if (!string.IsNullOrWhiteSpace(cr.Summary))
        {
            sb.AppendLine("## 一句话总结");
            sb.AppendLine();
            sb.AppendLine($"**{EscapeInline(cr.Summary)}**");
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
            sb.AppendLine($"- {EscapeInline(item)}");
        }
        sb.AppendLine();
    }

    private static void AppendQualityMetricsSection(StringBuilder sb, AnalysisQualityMetrics qm)
    {
        if (qm is null) return;
        if (qm.DataCompletenessPercent <= 0 && qm.AnalystConsensusPercent <= 0
            && string.IsNullOrWhiteSpace(qm.LimitationsNote)
            && qm.MissingDataDimensions is not { Count: > 0 })
        {
            return;
        }

        sb.AppendLine("## 分析质量自评估");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 值 |");
        sb.AppendLine("|------|------|");
        sb.AppendLine($"| 质量等级 | {EscapeTableCell(GetQualityLevelText(qm.OverallQualityLevel))} |");
        sb.AppendLine($"| 数据完整度 | {qm.DataCompletenessPercent}% |");
        sb.AppendLine($"| 分析师一致性 | {qm.AnalystConsensusPercent}% |");
        sb.AppendLine();

        if (qm.MissingDataDimensions is { Count: > 0 })
        {
            sb.AppendLine("**缺失的数据维度：**");
            sb.AppendLine();
            foreach (var dim in qm.MissingDataDimensions)
            {
                sb.AppendLine($"- {EscapeInline(dim)}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(qm.LimitationsNote))
        {
            sb.AppendLine("**分析局限性：**");
            sb.AppendLine();
            sb.AppendLine(EscapeInline(qm.LimitationsNote));
            sb.AppendLine();
        }
    }

    private static void AppendAnalystMessagesSection(StringBuilder sb, List<ChatMessage>? messages)
    {
        if (messages is not { Count: > 0 }) return;

        sb.AppendLine("## 各分析师详细分析");
        sb.AppendLine();
        foreach (var message in messages)
        {
            var author = string.IsNullOrWhiteSpace(message.AuthorName)
                ? GetRoleDisplayName(message.Role)
                : message.AuthorName;
            var text = message.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;

            sb.AppendLine($"### {EscapeInline(author)}");
            sb.AppendLine();
            sb.AppendLine(EscapeInline(text));
            sb.AppendLine();
        }
    }

    private static string GetQualityLevelText(AnalysisQualityLevel level) => level switch
    {
        AnalysisQualityLevel.High => "高（数据充分、分析师一致性高、置信度强）",
        AnalysisQualityLevel.Medium => "中（部分数据缺失或分析师存在一定分歧）",
        AnalysisQualityLevel.Low => "低（大量数据缺失或分析师严重分歧，结论仅供参考）",
        _ => "未知"
    };

    private static string GetRoleDisplayName(ChatRole role)
    {
        if (role == ChatRole.Assistant) return "分析师";
        if (role == ChatRole.User) return "用户";
        if (role == ChatRole.System) return "系统";
        if (role == ChatRole.Tool) return "工具";
        return role.Value ?? "未知";
    }

    /// <summary>
    /// 转义 Markdown 表格单元格中的特殊字符：管道符（会破坏表格结构）、反斜杠（转义符）、换行符。
    /// </summary>
    private static string EscapeTableCell(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    /// <summary>
    /// 转义行内 Markdown 文本中的特殊字符：反斜杠、星号、下划线、反引号、管道符。
    /// 不转义换行符，以保留段落结构。
    /// </summary>
    private static string EscapeInline(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("\\", "\\\\")
            .Replace("*", "\\*")
            .Replace("_", "\\_")
            .Replace("`", "\\`")
            .Replace("|", "\\|");
    }
}
