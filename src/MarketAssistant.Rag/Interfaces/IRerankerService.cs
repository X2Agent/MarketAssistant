using MarketAssistant.Rag;

namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 检索结果重排序接口（P1-02）：输入保留完整段落元数据的候选，输出按综合相关性重排后的候选。
/// </summary>
public interface IRerankerService
{
    /// <summary>
    /// 对检索候选进行重排。时效优先使用 <c>PublishedAt</c>，缺失时回退 URL/正文启发式。
    /// </summary>
    /// <param name="query">原始查询文本。</param>
    /// <param name="items">携带向量距离（越小越相关）的候选集。</param>
    /// <returns>按综合相关性重排后的候选。</returns>
    IReadOnlyList<RagSearchCandidate> Rerank(string query, IEnumerable<RagSearchCandidate> items);
}
