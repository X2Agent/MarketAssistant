using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// Kernel plugin that exposes high-quality retrieval functions for agents: local-only or local+web.
/// </summary>
public class GroundingSearchPlugin
{
    private readonly RetrievalOrchestrator _orchestrator;
    private readonly ILogger<GroundingSearchPlugin> _logger;

    public GroundingSearchPlugin(RetrievalOrchestrator orchestrator, ILogger<GroundingSearchPlugin> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [KernelFunction, Description("从内部知识库检索高质量证据片段（已进行多查询与重排）")]
    public async Task<KernelSearchResults<TextSearchResult>> GetGroundedResults(
        [Description("查询问题或关键字")] string query,
        [Description("集合名称（知识库名）")] string collectionName,
        [Description("返回条数")] int top = 6)
    {
        var items = await _orchestrator.RetrieveAsync(query, collectionName, top);
        return new KernelSearchResults<TextSearchResult>(AsAsync(items));
    }

    [KernelFunction, Description("结合网络可信来源，返回可溯源的高质量证据片段（多查询 + 可信过滤 + 重排）")]
    public async Task<KernelSearchResults<TextSearchResult>> GetGroundedResultsWithWeb(
        [Description("查询问题或关键字")] string query,
        [Description("集合名称（知识库名）")] string collectionName,
        [Description("返回条数")] int top = 6)
    {
        var items = await _orchestrator.RetrieveWithWebAsync(query, collectionName, top);
        return new KernelSearchResults<TextSearchResult>(AsAsync(items));
    }

    private static async IAsyncEnumerable<TextSearchResult> AsAsync(IEnumerable<TextSearchResult> items)
    {
        foreach (var i in items)
        {
            yield return i;
            await Task.CompletedTask;
        }
    }
}
