using MarketAssistant.Rag;
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
/// 2) 向量检索——对每个候选在内部向量集合中检索。
/// 3) 去重与重排：去重相似条目，重排获得优质结果。
/// </summary>
public class RetrievalOrchestrator : IRetrievalOrchestrator
{
    private readonly IQueryRewriteService _queryRewrite;
    private readonly IRerankerService _reranker;
    private readonly VectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<RetrievalOrchestrator> _logger;

    public RetrievalOrchestrator(
        IQueryRewriteService queryRewrite,
        IRerankerService reranker,
        VectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<RetrievalOrchestrator> logger)
    {
        _queryRewrite = queryRewrite;
        _reranker = reranker;
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
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
        var collection = _vectorStore.GetCollection<string, TextParagraph>(collectionName);
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        // 1) 查询重写——将原始查询重写出候选，提高召回。
        var rewrites = _queryRewrite.Rewrite(query, maxCandidates: 3);
        var queries = new List<string> { query };
        queries.AddRange(rewrites);

        // 2) 向量检索——对每个查询在向量集合中检索，合并结果。
        var merged = new List<TextSearchResult>();

        // VectorStoreTextSearch 无法自动推断向量字段的类型问题，推荐 SearchAsync 时指定 VectorProperty。
        //var vectorTextSearch = new VectorStoreTextSearch<TextParagraph>(collection, _embeddingGenerator);

        // 指定使用TextEmbedding字段进行向量搜索（避免向量字段推断问题）
        var vectorSearchOptions = new VectorSearchOptions<TextParagraph>
        {
            VectorProperty = r => r.TextEmbedding
        };

        // 对于每个查询的检索量，确保获得足够候选项供后续去重和重排
        // 多查询策略 + 向量搜索，能让查询获得更低的结果遗漏
        var perQueryLimit = Math.Max(top / 2, 3); // 至少3个，处理top很小时的边界情况

        foreach (var q in queries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                // 生成查询向量
                var queryVector = await _embeddingGenerator.GenerateAsync(q);

                // 使用SearchAsync方法，显式指定使用TextEmbedding向量字段
                var searchResults = collection.SearchAsync(
                    queryVector.Vector,
                    perQueryLimit,
                    vectorSearchOptions,
                    cancellationToken);

                await foreach (var searchResult in searchResults)
                {
                    var textResult = new TextSearchResult(searchResult.Record.Text)
                    {
                        Name = searchResult.Record.ParagraphId,
                        Link = searchResult.Record.DocumentUri
                    };
                    merged.Add(textResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vector search failed for subquery: {Query}", q);
            }
        }

        // 3) 如果检索为空的兜底提示。
        if (merged.Count == 0)
        {
            return Array.Empty<TextSearchResult>();
        }

        // 4) 标准去重：通过文本内容合并重复项
        var dedup = merged
            .GroupBy(r => $"{r.Link}|{r.Name}|{r.Value}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        // 5) 重排（支持RankGPT/启发式模型进一步优化重排）
        var reranked = _reranker.Rerank(query, dedup);
        return reranked.Take(top).ToList();
    }
}
