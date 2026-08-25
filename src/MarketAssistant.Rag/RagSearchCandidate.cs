using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Rag;

/// <summary>
/// 检索候选（P1-02）：保留摄取时的完整段落元数据，
/// 供重排使用 <c>PublishedAt</c> 时效、<c>Section/Order</c> 邻接上下文，
/// 转换为 <see cref="TextSearchResult"/> 只发生在管线最后一步。
/// </summary>
/// <param name="Record">向量库中的原始段落记录。</param>
/// <param name="VectorDistance">向量距离（CosineDistance，越小越相关）。</param>
/// <param name="MatchedQuery">命中该候选的查询文本（多路改写之一或原始查询）。</param>
public sealed record RagSearchCandidate(
    TextParagraph Record,
    double VectorDistance,
    string MatchedQuery);