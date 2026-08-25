using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// RAG 检索编排器，专注于知识库检索的核心管理
/// 包含经典的检索优化：
/// 1) 查询重写——将原始查询生成多个候选，提高召回。
/// 2) 向量检索——对每个候选在内部向量集合中检索，保留向量距离（越小越相关）。
/// 3) 去重与重排：去重相似条目，融合向量分数与启发式评分重排。
/// </summary>
public class RetrievalOrchestrator : IRetrievalOrchestrator
{
    private readonly IQueryRewriteService _queryRewrite;
    private readonly IRerankerService _reranker;
    private readonly IContextExpansionService _contextExpansion;
    private readonly VectorStore _vectorStore;
    private readonly IEmbeddingFactory _embeddingFactory;
    private readonly ILogger<RetrievalOrchestrator> _logger;

    public RetrievalOrchestrator(
        IQueryRewriteService queryRewrite,
        IRerankerService reranker,
        IContextExpansionService contextExpansion,
        VectorStore vectorStore,
        IEmbeddingFactory embeddingFactory,
        ILogger<RetrievalOrchestrator> logger)
    {
        _queryRewrite = queryRewrite;
        _reranker = reranker;
        _contextExpansion = contextExpansion ?? throw new ArgumentNullException(nameof(contextExpansion));
        _vectorStore = vectorStore;
        _embeddingFactory = embeddingFactory;
        _logger = logger;
    }

    /// <summary>
    /// 从内部向量集合中检索查询相关的段落。
    /// </summary>
    /// <param name="query">用户查询文本。</param>
    /// <param name="collectionName">向量集合名称。</param>
    /// <param name="top">重排后返回的条目数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>排序优化重排后的检索结果集。</returns>
    public async Task<IReadOnlyList<TextSearchResult>> RetrieveAsync(
        string query,
        string collectionName,
        int top = 8,
        CancellationToken cancellationToken = default)
    {
        // 创建嵌入生成器（使用当前最新配置）
        var embeddingGenerator = _embeddingFactory.Create();

        var collection = _vectorStore.GetCollection<string, TextParagraph>(collectionName);
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        // 1) 查询重写——将原始查询重写出候选，提高召回。
        var rewrites = _queryRewrite.Rewrite(query, maxCandidates: 3);
        var queries = new List<string> { query };
        queries.AddRange(rewrites);

        var distinctQueries = queries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 2) 批量生成查询向量，减少 HTTP 往返
        GeneratedEmbeddings<Embedding<float>>? queryEmbeddings = null;
        try
        {
            queryEmbeddings = await embeddingGenerator.GenerateAsync(distinctQueries, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量生成查询向量失败");
            return Array.Empty<TextSearchResult>();
        }

        // 指定使用TextEmbedding字段进行向量搜索（避免向量字段推断问题）
        var vectorSearchOptions = new VectorSearchOptions<TextParagraph>
        {
            VectorProperty = r => r.TextEmbedding
        };

        // 确保重排候选池足够大：每个查询召回 top*3，至少 10 条
        var perQueryLimit = Math.Max(top * 3, 10);

        // 3) 向量检索——对每个查询在向量集合中检索，合并结果并保留向量距离与完整元数据（P1-02）
        var merged = new List<RagSearchCandidate>();

        for (int qi = 0; qi < distinctQueries.Count; qi++)
        {
            var q = distinctQueries[qi];
            try
            {
                var queryVector = queryEmbeddings[qi];

                var searchResults = collection.SearchAsync(
                    queryVector.Vector,
                    perQueryLimit,
                    vectorSearchOptions,
                    cancellationToken);

                await foreach (var searchResult in searchResults)
                {
                    // P1-02：保留原始 Record（PublishedAt/Order/Section 等），最后一步才转换为 TextSearchResult
                    var distance = (double)(searchResult.Score ?? 0f);
                    merged.Add(new RagSearchCandidate(searchResult.Record, distance, q));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vector search failed for subquery: {Query}", q);
            }
        }

        // 4) 如果检索为空的兜底提示。
        if (merged.Count == 0)
        {
            return Array.Empty<TextSearchResult>();
        }

        // 5) 标准去重：优先按 Record.Key，其次 ContentHash，最后文本内容；保留距离最小（最相关）的
        var dedup = merged
            .GroupBy(
                c => c.Record.Key
                     ?? c.Record.ContentHash
                     ?? $"{c.Record.DocumentUri}|{c.Record.ParagraphId}|{c.Record.Text}",
                StringComparer.Ordinal)
            .Select(g => g.OrderBy(item => item.VectorDistance).First())
            .ToList();

        // 6) 重排：融合向量距离与启发式评分（时效优先 PublishedAt）
        var reranked = _reranker.Rerank(query, dedup);

        // 7) 邻接上下文扩展（P1-02）：为选中候选拼接同文档相邻段落，再转换为 TextSearchResult
        var selected = reranked.Take(top).ToList();
        var contextPool = dedup;
        var results = new List<TextSearchResult>(selected.Count);

        foreach (var candidate in selected)
        {
            var text = _contextExpansion.BuildExpandedText(candidate, contextPool);
            results.Add(new TextSearchResult(value: text)
            {
                Name = candidate.Record.ParagraphId,
                Link = candidate.Record.DocumentUri
            });
        }

        return results;
    }
}
