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

    [KernelFunction, Description("���ڲ�֪ʶ�����������֤��Ƭ�Σ��ѽ��ж��ѯ�����ţ�")]
    public async Task<KernelSearchResults<TextSearchResult>> GetGroundedResults(
        [Description("��ѯ�����ؼ���")] string query,
        [Description("�������ƣ�֪ʶ������")] string collectionName,
        [Description("��������")] int top = 6)
    {
        var items = await _orchestrator.RetrieveAsync(query, collectionName, top);
        return new KernelSearchResults<TextSearchResult>(AsAsync(items));
    }

    [KernelFunction, Description("������������Դ�����ؿ���Դ�ĸ�����֤��Ƭ�Σ����ѯ + ���Ź��� + ���ţ�")]
    public async Task<KernelSearchResults<TextSearchResult>> GetGroundedResultsWithWeb(
        [Description("��ѯ�����ؼ���")] string query,
        [Description("�������ƣ�֪ʶ������")] string collectionName,
        [Description("��������")] int top = 6)
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
