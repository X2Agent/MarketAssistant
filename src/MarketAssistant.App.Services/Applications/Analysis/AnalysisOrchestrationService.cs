using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Services.Archive;
using MarketAssistant.Services.Cache;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MarketAssistant.Applications.Analysis;

/// <summary>
/// 分析编排服务，封装工作流执行、缓存管理和报告归档逻辑，
/// 将 ViewModel 与工作流实现解耦
/// </summary>
public class AnalysisOrchestrationService : IDisposable
{
    private readonly MarketAnalysisWorkflow _workflow;
    private readonly IAnalysisCacheService _cacheService;
    private readonly ReportArchiveService _archiveService;
    private readonly ILogger<AnalysisOrchestrationService> _logger;

    /// <summary>
    /// 按标的代码加锁，防止缓存击穿：同一标的并发请求只执行一次工作流，其余等待缓存
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perAssetLocks = new();

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
    /// 执行分析：优先读缓存，缓存未命中则执行工作流。
    /// 使用按标的加锁防止缓存击穿，同一标的并发请求只执行一次工作流。
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

        // 按标的加锁：同一标的的并发请求串行化，第一个执行工作流，后续命中缓存
        var semaphore = _perAssetLocks.GetOrAdd(assetCode, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // 二次检查缓存（可能在等待锁期间已被其他请求填充）
            cached = await _cacheService.GetCachedAnalysisAsync(assetCode);
            if (cached != null)
            {
                _logger.LogInformation("从缓存加载分析结果（二次检查）: {AssetCode}", assetCode);
                return new AnalysisResult(cached, FromCache: true);
            }

            _logger.LogInformation("开始新的分析: {AssetCode}", assetCode);
            var report = await _workflow.AnalyzeAsync(assetCode, cancellationToken);

            // 先归档再缓存：归档失败时不缓存，避免"缓存命中但历史缺失"的幽灵报告
            // 归档失败会抛异常，此时不缓存，让用户下次重试
            try
            {
                await _archiveService.SaveAsync(report, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "归档失败，跳过缓存以避免幽灵报告: {AssetCode}", assetCode);
                throw;
            }

            await _cacheService.CacheAnalysisAsync(assetCode, report);

            return new AnalysisResult(report, FromCache: false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 加载历史报告
    /// </summary>
    public async Task<MarketAnalysisReport?> LoadHistoryReportAsync(
        long reportId, CancellationToken cancellationToken = default)
    {
        return await _archiveService.LoadAsync(reportId, cancellationToken);
    }

    /// <summary>
    /// 获取历史报告摘要
    /// </summary>
    public async Task<List<ReportSummary>> GetReportHistoryAsync(
        string assetCode, CancellationToken cancellationToken = default)
    {
        return await _archiveService.GetSummariesAsync(assetCode, 20, cancellationToken);
    }

    /// <summary>
    /// 释放所有按标的加锁的 SemaphoreSlim 资源，避免长期运行后内存泄漏。
    /// </summary>
    public void Dispose()
    {
        foreach (var kvp in _perAssetLocks)
            kvp.Value.Dispose();
        _perAssetLocks.Clear();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 分析结果封装
/// </summary>
public record AnalysisResult(MarketAnalysisReport Report, bool FromCache);
