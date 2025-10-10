using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 检索结果重排序的重排接口
/// </summary>
public interface IRerankerService
{
    IReadOnlyList<TextSearchResult> Rerank(string query, IEnumerable<TextSearchResult> items);
}
