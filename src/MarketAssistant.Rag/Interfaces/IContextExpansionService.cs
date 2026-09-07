namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 邻接上下文扩展服务（P1-02）：
/// 依据同文档的 <c>Order</c> 为命中段落拼接前后相邻段落文本，
/// 使表格/列表等块在送入协调器时带有表头与上下文。
/// </summary>
public interface IContextExpansionService
{
    /// <summary>
    /// 为选中候选构建扩展后的文本：前邻段落作为【上文】前置、后邻段落作为【下文】后置。
    /// 邻接来源限定同一 <c>DocumentUri</c> 且 <c>Order</c> 差值在窗口内；每侧截断至指定字符数。
    /// </summary>
    /// <param name="selected">选中的候选。</param>
    /// <param name="pool">本次检索合并得到的候选池（邻接来源，不产生额外 IO）。</param>
    /// <param name="window">邻接窗口大小（默认各取 1 条相邻段落）。</param>
    /// <param name="maxNeighborChars">每个邻接段落的最大字符数。</param>
    string BuildExpandedText(
        RagSearchCandidate selected,
        IReadOnlyList<RagSearchCandidate> pool,
        int window = 1,
        int maxNeighborChars = 300);
}