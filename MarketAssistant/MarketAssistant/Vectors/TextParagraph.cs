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
}
