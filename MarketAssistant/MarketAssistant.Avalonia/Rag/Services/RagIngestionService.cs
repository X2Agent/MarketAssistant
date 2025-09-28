using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Vectors.Services;

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
///   之后生成图片 Caption 与图像向量。图片路径由 <see cref="MarkdownDocumentBlockReader"/> 解析提供。
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

    // 保留：如未来扩展跨文档级别的去重，可在此处引入相关缓存

    public RagIngestionService(
        ITextCleaningService cleaning,
        ITextChunkingService chunking,
        ILogger<RagIngestionService> logger,
        DocumentBlockReaderFactory readerFactory,
        IImageEmbeddingService imageEmbeddingService)
    {
        _logger = logger;
        _readerFactory = readerFactory;
        _imageEmbeddingService = imageEmbeddingService;
        _blockMapper = new DocumentBlockMapper(cleaning, chunking);
    }

    /// <summary>
    /// 处理并写入指定文件内容到向量集合。
    /// </summary>
    /// <param name="collection">目标向量集合。</param>
    /// <param name="filePath">文件路径。</param>
    /// <param name="embeddingGenerator">文本嵌入生成器。</param>
    public async Task IngestFileAsync(
        VectorStoreCollection<string, TextParagraph> collection,
        string filePath,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        var blockReader = _readerFactory.GetReader(filePath);
        if (blockReader == null)
        {
            _logger.LogError("No block reader found for file: {File}", filePath);
            return;
        }

        await IngestWithBlocksAsync(collection, filePath, embeddingGenerator, blockReader);
    }

    /// <summary>
    /// 使用块读取器对文档进行多模态摄取（文本/表格/图片）。
    /// </summary>
    /// <param name="collection">目标向量集合。</param>
    /// <param name="filePath">文件路径。</param>
    /// <param name="embeddingGenerator">文本嵌入生成器。</param>
    /// <param name="blockReader">块读取器。</param>
    private async Task IngestWithBlocksAsync(
        VectorStoreCollection<string, TextParagraph> collection,
        string filePath,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IDocumentBlockReader blockReader)
    {
        var blocks = (await blockReader.ReadBlocksAsync(filePath)).OrderBy(b => b.Order).ToList();
        if (blocks.Count == 0) return;

        int currentOrder = 0;
        string? currentSection = null;
        var seenImageHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in blocks)
        {
            try
            {
                ImageMetadata? imageMetadata = null;

                // 处理图片块的去重和嵌入生成
                if (block is ImageBlock imageBlock && imageBlock.ImageBytes.Length > 0)
                {
                    imageMetadata = await ProcessImageBlockAsync(imageBlock, seenImageHashes);
                    if (imageMetadata == null) continue; // 跳过重复或处理失败的图片
                }

                // 使用DocumentBlockMapper处理所有类型的块
                var (paragraphs, nextOrder, updatedSection) = _blockMapper.MapBlock(
                    block, filePath, currentOrder, currentSection, imageMetadata);

                currentOrder = nextOrder;
                currentSection = updatedSection;

                // 为所有段落生成文本嵌入并存储
                foreach (var paragraph in paragraphs)
                {
                    paragraph.TextEmbedding = await embeddingGenerator.GenerateAsync(paragraph.Text);
                    await collection.UpsertAsync(paragraph);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process block at order {Order} in file {File}",
                    block.Order, filePath);
            }
        }
    }

    /// <summary>
    /// 处理图片块，包括去重检查和嵌入生成
    /// </summary>
    /// <param name="imageBlock">图片块</param>
    /// <param name="seenImageHashes">当前文档已见的图片哈希集合</param>
    /// <returns>图片元数据，如果跳过则返回null</returns>
    private async Task<ImageMetadata?> ProcessImageBlockAsync(ImageBlock imageBlock, HashSet<string> seenImageHashes)
    {
        var imageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(imageBlock.ImageBytes));

        // 检查重复：仅在当前文档内进行精确重复过滤
        if (!seenImageHashes.Add(imageHash))
        {
            return null; // 跳过当前文档内的精确重复图片
        }

        try
        {
            // 生成图片说明和嵌入
            var caption = await _imageEmbeddingService.CaptionAsync(imageBlock.ImageBytes);
            var imageEmbedding = await _imageEmbeddingService.GenerateAsync(imageBlock.ImageBytes);

            // 使用已解析的路径或生成默认路径
            var imagePath = imageBlock.ImagePath ?? $"image_{imageHash}.png";

            return new ImageMetadata(caption, imagePath, imageEmbedding);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process image metadata for image hash {ImageHash}", imageHash);
            return null;
        }
    }

}


