using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors;

public class TextParagraph
{
    /// <summary>A unique key for the text paragraph.</summary>
    [VectorStoreKey]
    public required string Key { get; init; }

    /// <summary>A uri that points at the original location of the document containing the text.</summary>
    [VectorStoreData]
    [TextSearchResultLink]
    public required string DocumentUri { get; init; }

    /// <summary>The id of the paragraph from the document containing the text.</summary>
    [VectorStoreData]
    [TextSearchResultName]
    public required string ParagraphId { get; init; }

    /// <summary>The text of the paragraph.</summary>
    [VectorStoreData(IsFullTextIndexed = true)]
    [TextSearchResultValue]
    public required string Text { get; init; }

    /// <summary>The embedding generated from the Text.</summary>
    [VectorStoreVector(1024, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw)]
    public Embedding<float> TextEmbedding { get; set; }

    /// <summary>
    /// Optional associated image uri (for cross-modal retrieval).
    /// </summary>
    [VectorStoreData]
    public string? ImageUri { get; init; }

    /// <summary>
    /// Optional image embedding for cross-modal search (same dimension as text for late fusion use-cases).
    /// </summary>
    [VectorStoreVector(1024, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw)]
    public Embedding<float>? ImageEmbedding { get; set; }

    /// <summary>
    /// Paragraph order index within the document (starting from 0). Settable to allow global ordering across mixed text/image blocks.
    /// </summary>
    [VectorStoreData]
    public int Order { get; set; }

    /// <summary>
    /// Optional section name or heading that this paragraph belongs to.
    /// </summary>
    [VectorStoreData]
    public string? Section { get; init; }

    /// <summary>
    /// Source type, e.g. "pdf", "docx", "web", "note".
    /// </summary>
    [VectorStoreData]
    public string? SourceType { get; init; }

    /// <summary>
    /// Content hash for deduplication.
    /// </summary>
    [VectorStoreData]
    public string? ContentHash { get; init; }

    /// <summary>
    /// 64-bit perceptual hash (aHash) for image duplicate / near-duplicate detection.
    /// </summary>
    [VectorStoreData]
    public string? ImagePerceptualHash { get; init; }

    /// <summary>
    /// Optional published or last-updated time if known.
    /// </summary>
    [VectorStoreData]
    public DateTimeOffset? PublishedAt { get; init; }

    // Table metadata
    [VectorStoreData]
    public bool IsTable { get; init; }

    [VectorStoreData]
    public string? TableCaption { get; init; }

    [VectorStoreData]
    public string? TableHash { get; init; }
}
