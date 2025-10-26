using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 市场分析工作流请求
/// </summary>
public sealed class MarketAnalysisRequest
{
    public string StockSymbol { get; init; } = string.Empty;
    public string? Prompt { get; init; }
}

/// <summary>
/// 单个分析师的分析结果（基于 Microsoft.Extensions.AI）
/// </summary>
public sealed class AnalystResult
{
    public string AnalystName { get; init; } = string.Empty;
    public AnalysisAgents AnalystType { get; init; }
    public string Content { get; init; } = string.Empty;
    public ChatRole Role { get; init; } = ChatRole.Assistant;
}

/// <summary>
/// 聚合后的分析结果
/// </summary>
public sealed class AggregatedAnalysisResult
{
    public MarketAnalysisRequest OriginalRequest { get; init; } = new();
    public List<AnalystResult> AnalystResults { get; init; } = new();
}

/// <summary>
/// 最终的市场分析报告（用于 ChatHistory 兼容性）
/// </summary>
public sealed class MarketAnalysisReport
{
    public string StockSymbol { get; init; } = string.Empty;
    public List<AnalystResult> AnalystResults { get; init; } = new();
    public string CoordinatorSummary { get; init; } = string.Empty;
    public ChatHistory ChatHistory { get; init; } = new(); // Semantic Kernel 的 ChatHistory
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
