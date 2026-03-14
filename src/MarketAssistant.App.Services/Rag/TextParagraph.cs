using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Rag;

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
    public Embedding<float> TextEmbedding { get; set; } = default!;

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
    public string? Section { get; set; }

    /// <summary>
    /// Source type, e.g. "pdf", "docx", "web", "note".
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public string? SourceType { get; init; }

    /// <summary>
    /// Content hash for deduplication.
    /// </summary>
    [VectorStoreData]
    public string? ContentHash { get; init; }

    /// <summary>
    /// Optional published or last-updated time if known (stored as ISO string).
    /// </summary>
    [VectorStoreData]
    public string? PublishedAt { get; init; }

    /// <summary>
    /// Block 类型编码：0=Text, 1=Heading, 2=List, 3=Table, 4=Image。用于查询与分析。
    /// </summary>
    [VectorStoreData]
    public int BlockKind { get; set; }

    /// <summary>
    /// 如果是标题块，标题级别（1-6）。
    /// </summary>
    [VectorStoreData]
    public int? HeadingLevel { get; set; }

    /// <summary>
    /// 如果是列表块，列表类型编码：0=Unordered, 1=Ordered。
    /// </summary>
    [VectorStoreData]
    public int? ListType { get; set; }
}
