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

    /// <summary>
    /// 从失败标记文本中提取失败分析师的 Agent 名称（ASCII 名，如 <c>FundamentalAnalyst</c>）。
    /// 标记格式见 <see cref="BuildFailureText"/>：前缀 + 空格 + "AgentName: reason"；
    /// 流式失败时标记前可能拼接了部分正文，故先定位标记再取其后段落。
    /// 非失败标记文本返回 null。
    /// </summary>
    public static string? ExtractAgentName(string? markerText)
    {
        if (string.IsNullOrWhiteSpace(markerText) || !IsFailureMarker(markerText))
            return null;

        var afterMarker = SliceAfterMarker(markerText);
        var separatorIndex = afterMarker.IndexOf(": ", StringComparison.Ordinal);
        return separatorIndex >= 0 ? afterMarker[..separatorIndex] : afterMarker;
    }

    /// <summary>
    /// 从失败标记文本中提取失败原因（不含 Agent 名称）；
    /// 非失败标记文本原样返回，空文本返回「未知错误」。
    /// </summary>
    public static string ExtractFailureReason(string? markerText)
    {
        if (string.IsNullOrEmpty(markerText))
            return "未知错误";

        if (!IsFailureMarker(markerText))
            return markerText;

        var afterMarker = SliceAfterMarker(markerText);
        var separatorIndex = afterMarker.IndexOf(": ", StringComparison.Ordinal);
        return separatorIndex >= 0 ? afterMarker[(separatorIndex + 2)..] : afterMarker;
    }

    /// <summary>
    /// 截取失败标记前缀之后（并去除起始空白）的文本段。
    /// </summary>
    private static string SliceAfterMarker(string markerText)
    {
        var markerIndex = markerText.IndexOf(FailureMarkerPrefix, StringComparison.Ordinal);
        return markerText[(markerIndex + FailureMarkerPrefix.Length)..].TrimStart();
    }

    private static string NormalizeReason(string reason)
    {
        var normalized = reason.ReplaceLineEndings(" ").Trim();
        return normalized.Length > 200 ? normalized[..200] : normalized;
    }
}
