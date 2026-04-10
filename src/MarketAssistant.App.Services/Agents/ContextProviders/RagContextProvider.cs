using MarketAssistant.Rag.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.ContextProviders;

/// <summary>
/// RAG 上下文提供者，实现 MAF AIContextProvider 模式。
/// 通过 MAF 标准接口接入 Agent，自动将 RAG 检索结果注入上下文，
/// 替代手动拼接检索上下文的方案。
/// </summary>
public class RagContextProvider : MessageAIContextProvider
{
    private readonly IRetrievalOrchestrator _retrievalOrchestrator;
    private readonly ILogger<RagContextProvider> _logger;
    private string _collectionName = "default";
    private int _topK = 5;
    private string? _currentQuery;

    /// <summary>
    /// 当前检索使用的向量集合名称
    /// </summary>
    public string CollectionName
    {
        get => _collectionName;
        set => _collectionName = value;
    }

    /// <summary>
    /// 返回的检索结果数量上限
    /// </summary>
    public int TopK
    {
        get => _topK;
        set => _topK = value;
    }

    /// <summary>
    /// 设置当前查询文本（在 Agent 调用前设置，Provider 执行时使用）
    /// </summary>
    public void SetCurrentQuery(string? query) => _currentQuery = query;

    public RagContextProvider(
        IRetrievalOrchestrator retrievalOrchestrator,
        ILogger<RagContextProvider> logger)
    {
        _retrievalOrchestrator = retrievalOrchestrator;
        _logger = logger;
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        // 从外部设置的查询文本获取检索关键词
        var query = ConsumeCurrentQuery();
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var results = await _retrievalOrchestrator.RetrieveAsync(
                query, _collectionName, _topK, cancellationToken);

            if (results.Count == 0)
            {
                _logger.LogDebug("RAG 检索无结果: {Query}", query);
                return [];
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## 知识库参考资料");
            sb.AppendLine("以下是从知识库中检索到的相关内容，请参考：");
            sb.AppendLine();

            foreach (var result in results)
            {
                if (!string.IsNullOrWhiteSpace(result.Name))
                    sb.AppendLine($"**来源**: {result.Name}");
                sb.AppendLine(result.Value);
                sb.AppendLine();
            }

            _logger.LogDebug("RAG 注入 {Count} 条检索结果到上下文", results.Count);

            return
            [
                new ChatMessage(ChatRole.System, sb.ToString())
            ];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG 检索失败，跳过知识库上下文注入");
            return [];
        }
    }

    /// <summary>
    /// 获取当前查询文本并清除
    /// </summary>
    private string? ConsumeCurrentQuery()
    {
        var query = _currentQuery;
        _currentQuery = null;
        return query;
    }
}
