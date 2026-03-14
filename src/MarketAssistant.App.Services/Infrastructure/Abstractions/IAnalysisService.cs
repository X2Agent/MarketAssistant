using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.Abstractions;

/// <summary>
/// 分析服务抽象接口，为未来模块化拆分提供边界。
/// UI 层应通过此接口而非直接引用 Agent 层。
/// </summary>
public interface IAnalysisService
{
    /// <summary>
    /// 执行资产分析
    /// </summary>
    Task<MarketAnalysisReport> AnalyzeAsync(string assetCode, CancellationToken ct = default);

    /// <summary>
    /// 分析进度事件
    /// </summary>
    event EventHandler<AnalysisProgressEventArgs>? ProgressChanged;
}
