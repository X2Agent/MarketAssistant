using CoenM.ImageHash.HashAlgorithms;
using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;
using Image = SixLabors.ImageSharp.Image;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// RAG 数据摄取：清洗 -> 语义分块 -> 嵌入 -> 入库。支持多模态（图片块）。
/// </summary>
/// <remarks>
/// 流程概览：
/// 1) 若存在 <see cref="IDocumentBlockReader"/>：按块读取文档（文本/表格/图片）。
///    - 文本块：调用 <see cref="ITextCleaningService"/> 清洗文本，使用 <see cref="ITextChunkingService"/> 分块，
///      然后为每个段落生成文本向量并 Upsert 到向量库。
///    - 表格块：将表格 Markdown 与可选标题拼接为文本，生成文本向量并 Upsert。
///    - 图片块：先用精确哈希（SHA-256）进行完全重复过滤，再用感知哈希（占位实现）判断近重复；
///      之后生成图片 Caption 与图像向量，并通过 <see cref="IImageStorageService"/> 持久化图片资源。
/// 2) 否则：使用 <see cref="IRawDocumentReader"/> 读取全文，清洗、分块后逐段生成向量并 Upsert。
///
/// 去重策略：
/// - 精确去重：对图片字节做 SHA-256，过滤完全重复。
/// - 感知去重：<see cref="ComputePerceptualHash(byte[])"/> 生成感知哈希，并由 <see cref="IsPerceptualDuplicate(string, System.Collections.Generic.HashSet{string})"/>
///   判断是否与已见集合足够接近（当前实现为占位，建议替换为 PHash/DHash 并按海明距离阈值判定）。
///
/// 设计原则：
/// - 摄取职责单一，吞吐/并发优化可在更高层实现；
/// - 元数据完备，<see cref="TextParagraph"/> 包含必要上下文（文档 URI、段落序号、表格/图片标记等）。
/// </remarks>
public class RagIngestionService : IRagIngestionService
{
    private readonly ITextCleaningService _cleaning;
    private readonly ITextChunkingService _chunking;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RagIngestionService> _logger;
    private readonly IEnumerable<IDocumentBlockReader> _blockReaders;
    private readonly IImageEmbeddingService _imageEmbeddingService;
    private readonly IImageStorageService _imageStorageService;
    private readonly HashSet<string> _seenPHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public RagIngestionService(
        ITextCleaningService cleaning,
        ITextChunkingService chunking,
        IServiceProvider serviceProvider,
        ILogger<RagIngestionService> logger,
        IEnumerable<IDocumentBlockReader> blockReaders,
        IImageEmbeddingService imageEmbeddingService,
        IImageStorageService imageStorageService)
    {
        _cleaning = cleaning;
        _chunking = chunking;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _blockReaders = blockReaders;
        _imageEmbeddingService = imageEmbeddingService;
        _imageStorageService = imageStorageService;
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
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var blockReader = _blockReaders.FirstOrDefault(r => r.CanRead(filePath));
        if (blockReader != null)
        {
            await IngestWithBlocksAsync(collection, filePath, embeddingGenerator, blockReader);
            return;
        }

        using var stream = File.OpenRead(filePath);
        var reader = _serviceProvider.GetKeyedService<IRawDocumentReader>(ext);
        if (reader is null)
        {
            _logger.LogError("No reader found for extension '{Ext}'. File: {File}", ext, filePath);
            return;
        }

        var allText = reader.ReadAllText(stream);
        var cleaned = _cleaning.Clean(allText);
        var chunks = _chunking.Chunk(filePath, cleaned).ToArray();

        foreach (var chunk in chunks)
        {
            chunk.TextEmbedding = await embeddingGenerator.GenerateAsync(chunk.Text);
            await collection.UpsertAsync(chunk);
        }
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
        using var stream = File.OpenRead(filePath);
        var blocks = blockReader.ReadBlocks(stream, filePath).OrderBy(b => b.Order).ToList();
        if (blocks.Count == 0) return;

        int textOrder = 0;
        // 已见图片的 SHA-256（精确）哈希，用于快速过滤完全重复图片。
        var seenImageHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            if (block.Type == DocumentBlockType.Text && !string.IsNullOrWhiteSpace(block.Text))
            {
                var cleaned = _cleaning.Clean(block.Text);
                foreach (var para in _chunking.Chunk(filePath, cleaned))
                {
                    para.Order = textOrder++;
                    para.TextEmbedding = await embeddingGenerator.GenerateAsync(para.Text);
                    await collection.UpsertAsync(para);
                }
            }
            else if (block.Type == DocumentBlockType.Table && !string.IsNullOrWhiteSpace(block.TableMarkdown))
            {
                var tableText = (block.TableCaption is not null ? block.TableCaption + "\n" : string.Empty) + block.TableMarkdown;
                var hash = block.TableHash ?? Sha256Hex(tableText);
                var paragraph = new TextParagraph
                {
                    Key = $"{Sha256Hex(filePath)}:tbl:{hash[..8]}",
                    DocumentUri = filePath,
                    ParagraphId = $"tbl_{textOrder}",
                    Text = tableText,
                    Order = textOrder++,
                    Section = null,
                    SourceType = "table",
                    ContentHash = hash,
                    IsTable = true,
                    TableCaption = block.TableCaption,
                    TableHash = hash,
                    PublishedAt = null
                };
                paragraph.TextEmbedding = await embeddingGenerator.GenerateAsync(paragraph.Text);
                await collection.UpsertAsync(paragraph);
            }
            else if (block.Type == DocumentBlockType.Image && block.ImageBytes is not null && block.ImageBytes.Length > 0)
            {
                var imageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(block.ImageBytes));
                var phash = ComputePerceptualHash(block.ImageBytes);
                if (!seenImageHashes.Add(imageHash) || IsPerceptualDuplicate(phash, _seenPHashes))
                {
                    continue; // skip duplicate or near-duplicate
                }
                try
                {
                    var caption = await _imageEmbeddingService.GenerateCaptionAsync(block.ImageBytes);
                    var embedding = await _imageEmbeddingService.GenerateAsync(block.ImageBytes);
                    var keyHash = imageHash;
                    // persist image using configured storage
                    string relative;
                    try
                    {
                        relative = await _imageStorageService.SaveImageAsync(block.ImageBytes, keyHash + ".png", CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to save image for ingestion: {File}", filePath);
                        continue;
                    }
                    var paragraph = new TextParagraph
                    {
                        Key = $"{Sha256Hex(filePath)}:img:{keyHash[..8]}",
                        DocumentUri = filePath,
                        ParagraphId = $"img_{textOrder}",
                        Text = caption,
                        Order = textOrder++,
                        Section = null,
                        SourceType = Path.GetExtension(filePath).Trim('.'),
                        ContentHash = keyHash,
                        ImagePerceptualHash = phash,
                        PublishedAt = null,
                        ImageUri = relative,
                        ImageEmbedding = embedding
                    };
                    paragraph.TextEmbedding = await embeddingGenerator.GenerateAsync(paragraph.Text);
                    await collection.UpsertAsync(paragraph);
                    if (!string.IsNullOrEmpty(phash)) _seenPHashes.Add(phash);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Image block ingestion failed: {File}", filePath);
                }
            }
        }
    }

    private static string Sha256Hex(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)));
    }

    private static string ComputePerceptualHash(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return string.Empty;
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            var hasher = new PerceptualHash();
            ulong hash = hasher.Hash(image);
            return hash.ToString("X16");
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool IsPerceptualDuplicate(string phashHex, HashSet<string> seenHashes)
    {
        if (string.IsNullOrEmpty(phashHex) || seenHashes.Count == 0) return false;
        if (!ulong.TryParse(phashHex, System.Globalization.NumberStyles.HexNumber, null, out var current)) return false;
        const int threshold = 12; // 可配置
        foreach (var s in seenHashes)
        {
            if (!ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var other)) continue;
            int dist = BitOperations.PopCount(current ^ other);
            if (dist <= threshold) return true;
        }
        return false;
    }
}


