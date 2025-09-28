using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// RAG 检索编排器接口：专注于知识库向量检索的核心功能
/// 针对金融文档分析场景进行优化，支持多查询变体和重排序
/// </summary>
public interface IRetrievalOrchestrator
{
    /// <summary>
    /// 在内部向量库中检索与查询相关的段落。
    /// 包含：查询重写 + 向量搜索 + 重排序
    /// </summary>
    /// <param name="query">用户查询文本。</param>
    /// <param name="collectionName">向量集合名称。</param>
    /// <param name="top">重排后返回的条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按相关性重排后的检索结果。</returns>
    Task<IReadOnlyList<TextSearchResult>> RetrieveAsync(
        string query,
        string collectionName,
        int top = 8,
        CancellationToken cancellationToken = default);
}
