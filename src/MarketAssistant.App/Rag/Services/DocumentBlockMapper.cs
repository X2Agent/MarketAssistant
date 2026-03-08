using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.AI;
using System.Security.Cryptography;
using System.Text;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 图片元数据，用于传递图片处理结果
/// </summary>
public record ImageMetadata(string Caption, string StoredPath, Embedding<float> ImageEmbedding);

/// <summary>
/// 将 DocumentBlock 转换为 TextParagraph 的映射器
/// </summary>
public class DocumentBlockMapper
{
    private readonly ITextCleaningService _cleaning;
    private readonly ITextChunkingService _chunking;

    public DocumentBlockMapper(ITextCleaningService cleaning, ITextChunkingService chunking)
    {
        _cleaning = cleaning;
        _chunking = chunking;
    }

    /// <summary>
    /// 将文档块转换为文本段落
    /// </summary>
    /// <param name="block">文档块</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="baseOrder">基础序号</param>
    /// <param name="currentSection">当前章节</param>
    /// <param name="imageMetadata">图片元数据（用于图片块）</param>
    /// <returns>转换后的段落集合和下一个序号</returns>
    public (IEnumerable<TextParagraph> Paragraphs, int NextOrder, string? UpdatedSection) 
        MapBlock(
            DocumentBlock block, 
            string filePath, 
            int baseOrder, 
            string? currentSection,
            ImageMetadata? imageMetadata = null)
    {
        var fileHash = Sha256Hex(filePath);
        var sourceType = GetSourceTypeFromPath(filePath);
        var paragraphs = new List<TextParagraph>();
        var nextOrder = baseOrder;
        var updatedSection = currentSection;

        switch (block)
        {
            case TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text):
                var cleaned = _cleaning.Clean(textBlock.Text);
                var chunks = _chunking.Chunk(filePath, cleaned);
                foreach (var chunk in chunks)
                {
                    // 创建新的段落对象以确保 ParagraphId 一致性
                    var newParagraph = new TextParagraph
                    {
                        Key = chunk.Key,
                        DocumentUri = chunk.DocumentUri,
                        ParagraphId = $"txt_{nextOrder}",
                        Text = chunk.Text,
                        TextEmbedding = chunk.TextEmbedding,
                        ImageUri = chunk.ImageUri,
                        ImageEmbedding = chunk.ImageEmbedding ?? new Embedding<float>(new float[1024]), // 确保不为null
                        Order = nextOrder++,
                        Section = currentSection,
                        SourceType = chunk.SourceType,
                        ContentHash = chunk.ContentHash,
                        PublishedAt = chunk.PublishedAt,
                        BlockKind = 0, // Text
                        HeadingLevel = chunk.HeadingLevel,
                        ListType = chunk.ListType
                    };
                    paragraphs.Add(newParagraph);
                }
                break;

            case HeadingBlock headingBlock when !string.IsNullOrWhiteSpace(headingBlock.Text):
                var headingText = _cleaning.Clean(headingBlock.Text).Trim();
                if (!string.IsNullOrEmpty(headingText))
                {
                    var hash = Sha256Hex(headingText);
                    paragraphs.Add(new TextParagraph
                    {
                        Key = $"{fileHash}:hdg:{headingBlock.Level}:{hash[..8]}",
                        DocumentUri = filePath,
                        ParagraphId = $"hdg_{nextOrder}",
                        Text = headingText,
                        Order = nextOrder++,
                        Section = currentSection,
                        SourceType = sourceType,
                        ContentHash = hash,
                        PublishedAt = null,
                        BlockKind = 1, // Heading
                        HeadingLevel = headingBlock.Level,
                        ImageEmbedding = new Embedding<float>(new float[1024]) // 空的图像嵌入
                    });
                    
                    // 更新当前章节：高级别标题会重置章节上下文
                    if (headingBlock.Level <= 3) // H1-H3 作为主要章节分割
                    {
                        updatedSection = headingText;
                    }
                }
                break;

            case ListBlock listBlock when listBlock.Items?.Count > 0:
                var listText = listBlock.Text;
                var cleanedList = _cleaning.Clean(listText).Trim();
                if (!string.IsNullOrEmpty(cleanedList))
                {
                    var hash = Sha256Hex(cleanedList);
                    paragraphs.Add(new TextParagraph
                    {
                        Key = $"{fileHash}:lst:{(listBlock.ListType == ListType.Ordered ? "o" : "u")}:{hash[..8]}",
                        DocumentUri = filePath,
                        ParagraphId = $"lst_{nextOrder}",
                        Text = cleanedList,
                        Order = nextOrder++,
                        Section = currentSection,
                        SourceType = sourceType,
                        ContentHash = hash,
                        PublishedAt = null,
                        BlockKind = 2, // List
                        ListType = (int)listBlock.ListType,
                        ImageEmbedding = new Embedding<float>(new float[1024]) // 空的图像嵌入
                    });
                }
                break;

            case TableBlock tableBlock when !string.IsNullOrWhiteSpace(tableBlock.Markdown):
                var tableText = tableBlock.Text; // 包含标题 + Markdown
                paragraphs.Add(new TextParagraph
                {
                    Key = $"{fileHash}:tbl:{tableBlock.Hash[..8]}",
                    DocumentUri = filePath,
                    ParagraphId = $"tbl_{nextOrder}",
                    Text = tableText,
                    Order = nextOrder++,
                    Section = currentSection,
                    SourceType = sourceType,
                    ContentHash = tableBlock.Hash,
                    PublishedAt = null,
                    BlockKind = 3, // Table
                    ImageEmbedding = new Embedding<float>(new float[1024]) // 空的图像嵌入
                });
                break;

            case ImageBlock imageBlock when imageBlock.ImageBytes.Length > 0:
                var imageHash = Convert.ToHexString(SHA256.HashData(imageBlock.ImageBytes));
                var imageParagraph = new TextParagraph
                {
                    Key = $"{fileHash}:img:{imageHash[..8]}",
                    DocumentUri = filePath,
                    ParagraphId = $"img_{nextOrder}",
                    Text = imageMetadata?.Caption ?? imageBlock.Text ?? "[图片]",
                    Order = nextOrder++,
                    Section = currentSection,
                    SourceType = sourceType,
                    ContentHash = imageHash,
                    PublishedAt = null,
                    BlockKind = 4, // Image
                    ImageUri = imageMetadata?.StoredPath,
                    ImageEmbedding = imageMetadata?.ImageEmbedding ?? new Embedding<float>(new float[1024]) // 确保不为null
                };
                paragraphs.Add(imageParagraph);
                break;
        }

        return (paragraphs, nextOrder, updatedSection);
    }

    private static string GetSourceTypeFromPath(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "pdf",
            ".docx" => "docx",
            ".md" => "markdown",
            ".txt" => "text",
            _ when filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) => "web",
            _ => "unknown"
        };

    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
}
