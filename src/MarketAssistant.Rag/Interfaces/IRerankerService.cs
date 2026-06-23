using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 携带向量相似度分数的检索结果，用于重排阶段融合向量分数与启发式评分。
/// </summary>
public record ScoredSearchResult(TextSearchResult Item, float VectorScore);

/// <summary>
/// 检索结果重排序的重排接口
/// </summary>
public interface IRerankerService
{
    /// <summary>
    /// 对携带向量相似度分数的检索结果进行重排。
    /// </summary>
    /// <param name="query">原始查询文本。</param>
    /// <param name="items">携带向量分数的检索结果集。</param>
    /// <returns>按综合相关性重排后的检索结果。</returns>
    IReadOnlyList<TextSearchResult> Rerank(string query, IEnumerable<ScoredSearchResult> items);
}
