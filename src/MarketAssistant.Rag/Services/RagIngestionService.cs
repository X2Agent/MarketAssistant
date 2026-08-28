using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using System.Security.Cryptography;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// RAG 数据摄取：清洗 -> 语义分块 -> 嵌入 -> 入库。支持多模态（图片块）。
/// </summary>
/// <remarks>
/// 流程概览：
/// 使用 <see cref="IDocumentBlockReader"/> 按块读取文档（文本/表格/图片）：
/// - 文本块：调用 <see cref="ITextCleaningService"/> 清洗文本，使用 <see cref="ITextChunkingService"/> 分块，
///   然后为每个段落生成文本向量并 Upsert 到向量库。
/// - 表格块：将表格 Markdown 与可选标题拼接为文本，生成文本向量并 Upsert。
/// - 图片块：仅用精确哈希（SHA-256）进行同一文档内的完全重复过滤；
///   之后生成图片 Caption（文本召回依据）与图像向量（CLIP 可用时）。
///   CLIP 失败不再降级为哈希向量，Caption 成功即可被文本检索命中。
///
/// 去重策略：
/// - 精确去重：对图片字节做 SHA-256，过滤同一文档内的完全重复。
///
/// 设计原则：
/// - 摄取职责单一，吞吐/并发优化可在更高层实现；
/// - 元数据完备，<see cref="TextParagraph"/> 包含必要上下文（文档 URI、段落序号、表格/图片标记等）。
/// - 使用LRU缓存管理感知哈希，避免内存泄漏。
/// - 图片路径解析由文档读取器负责，避免重复处理。
/// </remarks>
public class RagIngestionService : IRagIngestionService
{
    private readonly ILogger<RagIngestionService> _logger;
    private readonly DocumentBlockReaderFactory _readerFactory;
    private readonly IImageEmbeddingService _imageEmbeddingService;
    private readonly DocumentBlockMapper _blockMapper;
    private readonly IRagDocumentCatalog _documentCatalog;

    // 保留：如未来扩展跨文档级别的去重，可在此处引入相关缓存

    public RagIngestionService(
        ITextCleaningService cleaning,
        ITextChunkingService chunking,
        IRagDocumentCatalog documentCatalog,
        ILogger<RagIngestionService> logger,
        DocumentBlockReaderFactory readerFactory,
        IImageEmbeddingService imageEmbeddingService)
    {
        _logger = logger;
        _readerFactory = readerFactory;
        _imageEmbeddingService = imageEmbeddingService;
        _documentCatalog = documentCatalog ?? throw new ArgumentNullException(nameof(documentCatalog));
        _blockMapper = new DocumentBlockMapper(cleaning, chunking);
    }

    /// <summary>
    /// 处理并写入指定文件内容到向量集合，返回结构化摄取结果。
    /// </summary>
    /// <param name="collection">目标向量集合。</param>
    /// <param name="filePath">文件路径。</param>
    /// <param name="embeddingGenerator">文本嵌入生成器。</param>
    /// <param name="cancellationToken">取消令牌；取消以 <see cref="OperationCanceledException"/> 抛出。</param>
    public async Task<RagIngestionResult> IngestFileAsync(
        VectorStoreCollection<string, TextParagraph> collection,
        string collectionName,
        string filePath,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        var blockReader = _readerFactory.GetReader(filePath);
        if (blockReader == null)
        {
            _logger.LogError("No block reader found for file: {File}", filePath);
            return new RagIngestionResult(0, 0,
                new List<RagIngestionFailure> { new(0, "NoBlockReader", $"不支持的文档格式，无法读取：{Path.GetFileName(filePath)}") });
        }

        return await IngestWithBlocksAsync(collection, collectionName, filePath, embeddingGenerator, blockReader, cancellationToken);
    }

    /// <summary>
    /// 使用块读取器对文档进行多模态摄取（文本/表格/图片）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<RagIngestionResult> IngestWithBlocksAsync(
        VectorStoreCollection<string, TextParagraph> collection,
        string collectionName,
        string filePath,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IDocumentBlockReader blockReader,
        CancellationToken cancellationToken)
    {
        var blocks = (await blockReader.ReadBlocksAsync(filePath)).OrderBy(b => b.Order).ToList();
        if (blocks.Count == 0)
        {
            _logger.LogWarning("Document contains no blocks: {File}", filePath);
            return new RagIngestionResult(0, 0,
                new List<RagIngestionFailure> { new(0, "EmptyDocument", $"文档中没有可摄取的内容：{Path.GetFileName(filePath)}") });
        }

        int currentOrder = 0;
        string? currentSection = null;
        var seenImageHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<RagIngestionFailure>();
        var paragraphCount = 0;

        // P1-01：稳定文档标识与本次写入的 Key 集合（用于文档级替换）
        var documentId = RagDocumentId.Compute(filePath);
        var newKeys = new List<string>();
        string? embeddingModelId = null;
        int? embeddingDimension = null;

        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ImageMetadata? imageMetadata = null;

                // 处理图片块的去重和嵌入生成
                if (block is ImageBlock imageBlock && imageBlock.ImageBytes.Length > 0)
                {
                    imageMetadata = await ProcessImageBlockAsync(imageBlock, seenImageHashes, cancellationToken);
                    if (imageMetadata == null) continue; // 跳过重复或处理失败的图片
                }

                // 使用DocumentBlockMapper处理所有类型的块
                var (paragraphs, nextOrder, updatedSection) = _blockMapper.MapBlock(
                    block, filePath, documentId, currentOrder, currentSection, imageMetadata);

                currentOrder = nextOrder;
                currentSection = updatedSection;

                // 批量生成文本嵌入：收集所有段落文本后一次性调用嵌入 API，
                // 避免逐条 HTTP 往返触发限流，大幅提升大文档摄取性能
                var paragraphList = paragraphs.ToList();
                if (paragraphList.Count > 0)
                {
                    var texts = paragraphList.Select(p => p.Text).ToList();
                    var embeddings = await embeddingGenerator.GenerateAsync(texts, cancellationToken: cancellationToken);

                    // 数量校验：返回条数与文本条数不一致时不写入，避免索引错位
                    if (embeddings.Count != paragraphList.Count)
                    {
                        failures.Add(new RagIngestionFailure(block.Order, "EmbeddingCountMismatch",
                            $"嵌入生成数量不匹配（期望 {paragraphList.Count}，实际 {embeddings.Count}），该块未写入。"));
                        _logger.LogWarning(
                            "Embedding count mismatch for file {File}, block order {Order}: expected {Expected}, got {Actual}",
                            filePath, block.Order, paragraphList.Count, embeddings.Count);
                        continue;
                    }

                    // 维度校验：在 Upsert 前失败并给出可操作提示，避免脏数据入库
                    var mismatch = embeddings.FirstOrDefault(e => e.Vector.Length != RagConstants.EmbeddingDimension);
                    if (mismatch is not null)
                    {
                        failures.Add(new RagIngestionFailure(block.Order, "EmbeddingDimensionMismatch",
                            $"Embedding 维度不匹配。当前知识库只支持 {RagConstants.EmbeddingDimension} 维，实际为 {mismatch.Vector.Length}。" +
                            "请改回支持的 Embedding 模型，或重建向量库后再切换。该块未写入。"));
                        _logger.LogWarning(
                            "Embedding dimension mismatch for file {File}, block order {Order}: expected {Expected}, got {Actual}",
                            filePath, block.Order, RagConstants.EmbeddingDimension, mismatch.Vector.Length);
                        continue;
                    }

                    for (int i = 0; i < paragraphList.Count; i++)
                    {
                        paragraphList[i].TextEmbedding = embeddings[i];
                        await collection.UpsertAsync(paragraphList[i], cancellationToken);
                        newKeys.Add(paragraphList[i].Key);
                        paragraphCount++;
                    }

                    embeddingDimension ??= embeddings[0].Vector.Length;
                    embeddingModelId ??= (embeddingGenerator.GetService(typeof(EmbeddingGeneratorMetadata)) as EmbeddingGeneratorMetadata)?.DefaultModelId
                                         ?? embeddingGenerator.GetType().Name;
                }
            }
            catch (OperationCanceledException)
            {
                // 取消不属于块失败，必须继续向上传播
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new RagIngestionFailure(block.Order, "BlockProcessingError",
                    $"块处理失败（顺序 {block.Order}）：{ex.Message}"));
                _logger.LogWarning(ex, "Failed to process block at order {Order} in file {File}",
                    block.Order, filePath);
            }
        }

        // P1-01 文档级替换：有任何段落入库时，删除旧版本残留 Key 并更新清单；
        // 全失败（IsFailure）时保留旧清单，便于下次重试后恢复一致。
        if (paragraphCount > 0)
        {
            try
            {
                var oldKeys = await _documentCatalog.GetKeysAsync(collectionName, documentId, cancellationToken);
                var staleKeys = oldKeys.Except(newKeys).ToList();
                foreach (var staleKey in staleKeys)
                {
                    await collection.DeleteAsync(staleKey, cancellationToken);
                }

                if (staleKeys.Count > 0)
                {
                    _logger.LogInformation(
                        "文档级替换：{File} 删除旧段落 {StaleCount} 个（新段落 {NewCount} 个）",
                        filePath, staleKeys.Count, newKeys.Count);
                }

                // 文档内容哈希：以原始字节流计算 SHA-256，避免整文件读入内存
                string contentHash;
                using (var fileStream = File.OpenRead(filePath))
                {
                    contentHash = Convert.ToHexString(SHA256.HashData(fileStream));
                }

                await _documentCatalog.ReplaceAsync(new RagDocumentCatalogEntry(
                    collectionName,
                    documentId,
                    filePath,
                    RagDocumentId.Compute(contentHash),
                            newKeys,
                            embeddingModelId ?? "unknown",
                            embeddingDimension ?? RagConstants.EmbeddingDimension,
                            DateTimeOffset.UtcNow), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception catalogEx)
            {
                // 清单更新失败不回滚已写入的段落（幂等重试可自愈），但要显式告警
                _logger.LogError(catalogEx, "文档清单更新失败: {File}（下次摄取将重新执行替换）", filePath);
            }
        }

        return new RagIngestionResult(blocks.Count, paragraphCount, failures);
    }

    /// <summary>
    /// 处理图片块，包括去重检查和嵌入生成
    /// </summary>
    /// <param name="imageBlock">图片块</param>
    /// <param name="seenImageHashes">当前文档已见的图片哈希集合</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>图片元数据，如果跳过则返回null</returns>
    private async Task<ImageMetadata?> ProcessImageBlockAsync(ImageBlock imageBlock, HashSet<string> seenImageHashes, CancellationToken cancellationToken)
    {
        var imageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(imageBlock.ImageBytes));

        // 检查重复：仅在当前文档内进行精确重复过滤
        if (!seenImageHashes.Add(imageHash))
        {
            return null; // 跳过当前文档内的精确重复图片
        }

        try
        {
            // 生成图片说明（Caption 成功即可被文本检索命中）
            var caption = await _imageEmbeddingService.CaptionAsync(imageBlock.ImageBytes, cancellationToken);

            // 图像向量独立生成：CLIP 失败不降级为哈希向量，置 null 并依赖 Caption 召回
            Embedding<float>? imageEmbedding = null;
            try
            {
                imageEmbedding = await _imageEmbeddingService.GenerateAsync(imageBlock.ImageBytes, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception embedEx)
            {
                _logger.LogWarning(embedEx, "图像向量生成失败，本图仅依赖 Caption 文本召回");
            }

            // 使用已解析的路径或生成默认路径
            var imagePath = imageBlock.ImagePath ?? $"image_{imageHash}.png";

            return new ImageMetadata(caption, imagePath, imageEmbedding);
        }
        catch (OperationCanceledException)
        {
            // 取消必须向上传播，不得当作图片处理失败吞掉
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process image metadata for image hash {ImageHash}", imageHash);
            return null;
        }
    }

}


