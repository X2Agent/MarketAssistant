using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Services.Archive;
using MarketAssistant.Services.Cache;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Analysis;

/// <summary>
/// 分析编排服务，封装工作流执行、缓存管理和报告归档逻辑，
/// 将 ViewModel 与工作流实现解耦
/// </summary>
public class AnalysisOrchestrationService
{
    private readonly MarketAnalysisWorkflow _workflow;
    private readonly IAnalysisCacheService _cacheService;
    private readonly ReportArchiveService _archiveService;
    private readonly ILogger<AnalysisOrchestrationService> _logger;

    public AnalysisOrchestrationService(
        MarketAnalysisWorkflow workflow,
        IAnalysisCacheService cacheService,
        ReportArchiveService archiveService,
        ILogger<AnalysisOrchestrationService> logger)
    {
        _workflow = workflow;
        _cacheService = cacheService;
        _archiveService = archiveService;
        _logger = logger;
    }

    /// <summary>
    /// 工作流进度事件（透传给 UI 层）
    /// </summary>
    public event EventHandler<AnalysisProgressEventArgs>? ProgressChanged
    {
        add => _workflow.ProgressChanged += value;
        remove => _workflow.ProgressChanged -= value;
    }

    /// <summary>
    /// 执行分析：优先读缓存，缓存未命中则执行工作流
    /// </summary>
    public async Task<AnalysisResult> AnalyzeAsync(
        string assetCode, CancellationToken cancellationToken = default)
    {
        var cached = await _cacheService.GetCachedAnalysisAsync(assetCode);
        if (cached != null)
        {
            _logger.LogInformation("从缓存加载分析结果: {AssetCode}", assetCode);
            return new AnalysisResult(cached, FromCache: true);
        }

        _logger.LogInformation("开始新的分析: {AssetCode}", assetCode);
        var report = await _workflow.AnalyzeAsync(assetCode, cancellationToken);

        await _archiveService.SaveAsync(report);
        _ = _cacheService.CacheAnalysisAsync(assetCode, report);

        return new AnalysisResult(report, FromCache: false);
    }

    /// <summary>
    /// 加载历史报告
    /// </summary>
    public async Task<MarketAnalysisReport?> LoadHistoryReportAsync(long reportId)
    {
        return await _archiveService.LoadAsync(reportId);
    }

    /// <summary>
    /// 获取历史报告摘要
    /// </summary>
    public async Task<List<ReportSummary>> GetReportHistoryAsync(string assetCode)
    {
        return await _archiveService.GetSummariesAsync(assetCode);
    }
}

/// <summary>
/// 分析结果封装
/// </summary>
public record AnalysisResult(MarketAnalysisReport Report, bool FromCache);
