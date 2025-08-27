using MarketAssistant.Vectors.Interfaces;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 基于 Semantic Kernel 的轻量查询改写服务。
/// </summary>
public class QueryRewriteService : IQueryRewriteService
{
    private readonly Kernel _kernel;

    public QueryRewriteService(Kernel kernel)
    {
        _kernel = kernel;
    }

    /// <summary>
    /// 生成若干改写候选（同义词/扩展关键词/简洁版）。
    /// </summary>
    /// <param name="query">原始查询</param>
    /// <param name="maxCandidates">最多返回多少个候选，默认为 3</param>
    /// <returns>改写后的查询候选列表</returns>
    public async Task<IReadOnlyList<string>> RewriteAsync(string query, int maxCandidates = 3)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();

        var prompt = $$"""
你是检索查询优化助手。请基于以下用户查询，生成{maxCandidates}个不同风格的检索查询候选：
- 候选需短小、去除冗余、覆盖同义词与相关术语
- 同时考虑中文/英文写法、股票代码/名称别称
- 包含一个时间敏感变体（例如加上“最近3个月/近1周”）
- 用换行分隔每个候选，不要编号，不要解释

用户查询：{query}
""";

        var result = await _kernel.InvokePromptAsync(prompt);
        var text = result.GetValue<string>() ?? string.Empty;
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim('-','•','*',' '))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCandidates)
            .ToArray();

        return lines;
    }
}




