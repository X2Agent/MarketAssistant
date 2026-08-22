namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 分析师失败标记消息契约：由 <see cref="AIAgentFailureIsolation"/> 包装器生成，
/// 由市场分析聚合器识别。用于在不中断 Fan-In 工作流的前提下传递分析师失败信息。
/// </summary>
public static class AnalystFailureMessages
{
    /// <summary>
    /// 失败标记前缀。选用方括号英文大写形式，避免与正常分析师输出冲突。
    /// </summary>
    public const string FailureMarkerPrefix = "[ANALYST_FAILURE]";

    /// <summary>
    /// 聚合器附加给协调分析师的「维度缺失说明」前缀。
    /// </summary>
    public const string MissingDimensionNotePrefix = "[MISSING_DIMENSION_NOTE]";

    /// <summary>
    /// 判断消息文本是否包含失败标记。
    /// 用 Contains 而非 StartsWith：流式路径下分析师可能先输出部分正文再抛异常，
    /// 最终消息为「部分文本 + 标记」，前缀检测会漏判并把半成品当成功结论。
    /// </summary>
    public static bool IsFailureMarker(string? text)
        => text is not null && text.Contains(FailureMarkerPrefix, StringComparison.Ordinal);

    /// <summary>
    /// 构建失败标记文本（单行，超长截断，防止异常堆栈污染工作流消息）。
    /// </summary>
    public static string BuildFailureText(string agentName, string reason)
        => $"{FailureMarkerPrefix} {agentName}: {NormalizeReason(reason)}";

    /// <summary>
    /// 构建附加给协调分析师的维度缺失说明，要求其在报告中如实标注数据局限。
    /// </summary>
    public static string BuildMissingDimensionNote(IReadOnlyList<string> failedAnalystDescriptions)
        => $"{MissingDimensionNotePrefix} 以下分析师本次执行失败，其维度结论缺失：{string.Join("；", failedAnalystDescriptions)}。" +
           "综合报告必须明确标注该数据局限，不得虚构缺失维度的结论。";

    private static string NormalizeReason(string reason)
    {
        var normalized = reason.ReplaceLineEndings(" ").Trim();
        return normalized.Length > 200 ? normalized[..200] : normalized;
    }
}
