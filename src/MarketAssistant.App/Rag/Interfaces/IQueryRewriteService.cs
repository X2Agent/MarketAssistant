namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 查询改写/扩展服务接口。
/// </summary>
public interface IQueryRewriteService
{
    /// <summary>
    /// 将用户查询改写为更有利于检索的查询（可能返回多个候选查询）。
    /// </summary>
    /// <param name="query">原始查询</param>
    /// <param name="maxCandidates">最大候选数</param>
    /// <returns>改写后的候选查询集合（去重）</returns>
    IReadOnlyList<string> Rewrite(string query, int maxCandidates = 3);
}


