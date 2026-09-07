using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 向量化结果汇总。
/// </summary>
public sealed record VectorizationResult(
    int TotalCount,
    int SuccessCount,
    int PartialCount,
    int FailedCount,
    IReadOnlyList<string> FailedFiles,
    IReadOnlyList<string> PartialFiles)
{
    public bool AllSucceeded => FailedCount == 0 && PartialCount == 0;
}

/// <summary>
/// 文档向量化服务：枚举知识库目录、逐文件向量化并汇总结果（纯业务逻辑，不依赖 UI 通知）。
/// 进度通过 <see cref="IProgress{T}"/> 回调上报，结果以 <see cref="VectorizationResult"/> 返回，由调用方（ViewModel）负责 UI 反馈。
/// </summary>
public sealed class DocumentVectorizationService
{
    public static readonly string[] SupportedExtensions = [".pdf", ".docx", ".md"];

    // 向量化在途守卫必须跨服务实例生效（与 ViewModel 生命周期的并发向量化场景一致）
    private static int _activeVectorizations;

    private readonly IRagInfrastructureProvider _ragInfrastructureProvider;
    private readonly ILogger<DocumentVectorizationService> _logger;

    public DocumentVectorizationService(
        IRagInfrastructureProvider ragInfrastructureProvider,
        ILogger<DocumentVectorizationService> logger)
    {
        _ragInfrastructureProvider = ragInfrastructureProvider;
        _logger = logger;
    }

    /// <summary>
    /// 尝试进入向量化（跨实例并发守卫），返回 false 表示已有任务在进行中。
    /// </summary>
    public bool TryBeginVectorization() =>
        Interlocked.CompareExchange(ref _activeVectorizations, 1, 0) == 0;

    /// <summary>
    /// 结束向量化，释放并发守卫。
    /// </summary>
    public void EndVectorization() =>
        Interlocked.Exchange(ref _activeVectorizations, 0);

    /// <summary>
    /// 向量化指定目录下所有支持的文档，返回汇总结果；目录中无支持文档时返回 null。
    /// </summary>
    public async Task<VectorizationResult?> VectorizeDirectoryAsync(
        string directory,
        string collectionName,
        IProgress<(int Percent, string Text)>? progress,
        CancellationToken ct)
    {
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        if (files.Count == 0)
            return null;

        // 创建嵌入生成器（只在实际需要时创建）
        var embeddingGenerator = _ragInfrastructureProvider.GetEmbeddingFactory().Create();

        var collection = _ragInfrastructureProvider.GetVectorStore().GetCollection<string, TextParagraph>(collectionName);
        await collection.EnsureCollectionExistsAsync();
        _logger.LogInformation("使用向量集合: {CollectionName}", collectionName);

        var ragIngestionService = _ragInfrastructureProvider.GetIngestionService();
        var totalFiles = files.Count;
        _logger.LogInformation("找到 {Count} 个文档需要向量化", totalFiles);

        var successCount = 0;
        var partialCount = 0;
        var failedCount = 0;
        var failedFiles = new List<string>();
        var partialFiles = new List<string>();

        for (int i = 0; i < totalFiles; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            var fileName = Path.GetFileName(file);
            var fileExtension = Path.GetExtension(file).ToUpperInvariant();

            try
            {
                var currentIndex = i + 1;
                progress?.Report(((int)((double)currentIndex / totalFiles * 100), $"正在处理 {currentIndex}/{totalFiles}: {fileName}"));

                _logger.LogInformation("正在处理 ({Index}/{Total}): {FileName} [{Extension}]",
                    currentIndex, totalFiles, fileName, fileExtension);

                // 执行向量化：根据结构化结果区分完全成功/部分成功/失败
                var result = await ragIngestionService.IngestFileAsync(
                    collection, collectionName, file, embeddingGenerator, ct);

                if (result.IsSuccess)
                {
                    successCount++;
                    _logger.LogInformation("✓ 成功向量化: {FileName}", fileName);
                }
                else if (result.IsPartialSuccess)
                {
                    // 部分成功不计入完全成功
                    partialCount++;
                    partialFiles.Add($"{fileName}（{result.Failures.Count} 个块失败）");
                    _logger.LogWarning("△ 部分成功向量化: {FileName}，{BlockCount} 块中 {Failed} 个失败",
                        fileName, result.BlockCount, result.Failures.Count);
                }
                else
                {
                    failedCount++;
                    failedFiles.Add(fileName);
                    var reason = result.Failures.FirstOrDefault()?.Message ?? "没有内容入库";
                    _logger.LogError("✗ 向量化失败: {FileName} - {Reason}", fileName, reason);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 取消向上传播，由调用方统一处理
                throw;
            }
            catch (Exception ex)
            {
                failedCount++;
                failedFiles.Add(fileName);
                _logger.LogError(ex, "✗ 向量化失败: {FileName} - {ErrorMessage}", fileName, ex.Message);
                // 单个文件失败不中断整体流程，继续处理下一个
            }
        }

        _logger.LogInformation("向量化完成：成功 {Success}/{Total} 个，部分成功 {Partial} 个，失败 {Failed} 个",
            successCount, totalFiles, partialCount, failedCount);

        return new VectorizationResult(totalFiles, successCount, partialCount, failedCount, failedFiles, partialFiles);
    }
}
