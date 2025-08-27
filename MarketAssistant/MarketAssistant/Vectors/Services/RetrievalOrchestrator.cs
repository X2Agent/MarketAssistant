using MarketAssistant.Infrastructure;
using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// RAG 检索编排器，负责端到端的检索流程。
/// 步骤：
/// 1) 查询改写：基于原始查询生成多个候选，提升召回。
/// 2) 向量检索：对每个候选在内部集合检索并合并结果。
/// 3) 可选晚融合：对包含图片的项按“文本相似度 + 图像相似度”加权融合打分。
/// 4) 去重与重排：去除冗余条目，调用重排服务得到最终排序。
/// 5) 可选 Web 扩充：在需要时加入外部可信来源检索结果。
/// </summary>
public class RetrievalOrchestrator
{
    private readonly IQueryRewriteService _queryRewrite;
    private readonly IRerankerService _reranker;
    private readonly VectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IWebTextSearchFactory _webTextSearchFactory;
    private readonly ILogger<RetrievalOrchestrator> _logger;

    // 晚融合的权重设置：
    // 文本占比 0.7，图像占比 0.3（在多模态条目存在时更稳健）。
    private const double TextWeight = 0.7;
    private const double ImageWeight = 0.3;

    public RetrievalOrchestrator(
        IQueryRewriteService queryRewrite,
        IRerankerService reranker,
        VectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IWebTextSearchFactory webTextSearchFactory,
        ILogger<RetrievalOrchestrator> logger)
    {
        _queryRewrite = queryRewrite;
        _reranker = reranker;
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _webTextSearchFactory = webTextSearchFactory;
        _logger = logger;
    }

    /// <summary>
    /// 在内部向量库中检索与查询相关的段落。
    /// </summary>
    /// <param name="query">用户查询文本。</param>
    /// <param name="collectionName">向量集合名称。</param>
    /// <param name="top">重排后返回的条数。</param>
    /// <param name="enableLateFusion">是否启用文本/图像相似度的晚融合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按相关性重排后的检索结果。</returns>
    public async Task<IReadOnlyList<TextSearchResult>> RetrieveAsync(
        string query,
        string collectionName,
        int top = 8,
        bool enableLateFusion = true,
        CancellationToken cancellationToken = default)
    {
        var collection = _vectorStore.GetCollection<string, TextParagraph>(collectionName);
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        // 1) 多查询改写：包含原始查询与改写候选，以提升召回。
        var rewrites = await _queryRewrite.RewriteAsync(query, maxCandidates: 4);
        var queries = new List<string> { query };
        queries.AddRange(rewrites);

        // 2) 混合向量检索：对每个查询变体进行检索，并合并结果。
        var merged = new List<TextSearchResult>();
        var vectorTextSearch = new VectorStoreTextSearch<TextParagraph>(collection, _embeddingGenerator);
        foreach (var q in queries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var results = await vectorTextSearch.GetTextSearchResultsAsync(q, new() { Top = top / 2 + 2 }, cancellationToken);
                await foreach (var r in results.Results)
                {
                    merged.Add(r);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vector search failed for subquery: {Query}", q);
            }
        }

        // 3) 本地检索为空的兜底提示。
        if (merged.Count == 0)
        {
            return new List<TextSearchResult>
            {
                new("未检索到相关资料。请更换关键词或检查知识库是否已完成摄取。") { Name = "未检索到相关资料" }
            };
        }

        // 4) 可选“晚融合”：若条目包含图像向量，则将“文本相似度”和“图像相似度”加权融合。
        //    通过反射访问可选属性，兼容不同 SDK 版本下的字段变化。
        if (enableLateFusion)
        {
            try
            {
                var queryTextEmbedding = await _embeddingGenerator.GenerateAsync(query);
                var qt = queryTextEmbedding.Vector.ToArray();
                foreach (var res in merged)
                {
                    var imgVec = TryGetEmbeddingFromProperties(res, "ImageEmbedding");
                    if (imgVec.Length == 0) continue;

                    var textVec = TryGetEmbeddingFromProperties(res, "Embedding");
                    double textScore = TryGetNullableDouble(res, "Score") ?? Cosine(qt, textVec);
                    var imgScore = Cosine(qt, imgVec);
                    var fused = TextWeight * textScore + ImageWeight * imgScore;
                    TrySetNullableDouble(res, "Score", fused);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Late fusion failed; continue without image adjustment");
            }
        }

        // 5) 去重：用 link+name+snippet 作为键，避免重复条目。
        var dedup = merged
            .GroupBy(r => $"{r.Link}|{r.Name}|{r.Value}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        // 6) 重排与截断：调用重排服务得到最终排序后，取前 N 条。
        var reranked = await _reranker.RerankAsync(query, dedup, cancellationToken);
        return reranked.Take(top).ToList();
    }

    /// <summary>
    /// 尝试从结果的可选 Properties 字段读取 Embedding<float>，并返回其向量。
    /// 若缺失或类型不匹配，返回空数组。
    /// </summary>
    private static float[] TryGetEmbeddingFromProperties(TextSearchResult r, string key)
    {
        try
        {
            var props = r.GetType().GetProperty("Properties")?.GetValue(r) as IReadOnlyDictionary<string, object?>;
            if (props != null && props.TryGetValue(key, out var obj) && obj is Embedding<float> emb)
            {
                return emb.Vector.ToArray();
            }
        }
        catch { }
        return Array.Empty<float>();
    }

    /// <summary>
    /// 通过反射安全读取可空 double 属性（如 Score）。
    /// </summary>
    private static double? TryGetNullableDouble(object obj, string propertyName)
    {
        try
        {
            var p = obj.GetType().GetProperty(propertyName);
            if (p != null && p.PropertyType == typeof(double?))
            {
                return (double?)p.GetValue(obj);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 通过反射安全设置可空 double 属性（如 Score）。
    /// </summary>
    private static void TrySetNullableDouble(object obj, string propertyName, double value)
    {
        try
        {
            var p = obj.GetType().GetProperty(propertyName);
            if (p != null && p.PropertyType == typeof(double?) && p.CanWrite)
            {
                p.SetValue(obj, value);
            }
        }
        catch { }
    }

    private static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>
    /// 先从本地向量库检索，再按需并入筛选后的 Web 结果。
    /// </summary>
    /// <param name="query">用户查询文本。</param>
    /// <param name="collectionName">向量集合名称。</param>
    /// <param name="top">重排后返回的条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<TextSearchResult>> RetrieveWithWebAsync(
        string query,
        string collectionName,
        int top = 8,
        CancellationToken cancellationToken = default)
    {
        var local = await RetrieveAsync(query, collectionName, top, enableLateFusion: true, cancellationToken);
        if (local.Count == 1 && local[0].Name == "未检索到相关资料")
        {
            // 保留 fallback 文本，但继续尝试 Web 结果
        }

        // 若可用则进行 Web 检索
        var textSearch = _webTextSearchFactory.Create();
        if (textSearch is null)
        {
            return local;
        }

        // 与本地检索保持一致：基于改写候选进行多查询 Web 检索。
        var rewrites = await _queryRewrite.RewriteAsync(query, maxCandidates: 3);
        var queries = new List<string> { query };
        queries.AddRange(rewrites);

        var webResults = new List<TextSearchResult>();
        foreach (var q in queries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var plugin = textSearch.CreateWithGetTextSearchResults("WebSearchPlugin");
                var fr = await plugin["GetTextSearchResults"].InvokeAsync(
                    new Kernel(), new() { ["query"] = q }, cancellationToken: cancellationToken);
                var ksr = fr.GetValue<KernelSearchResults<TextSearchResult>>();
                if (ksr is not null)
                {
                    await foreach (var r in ksr.Results)
                    {
                        webResults.Add(r);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web search failed for subquery: {Query}", q);
            }
        }

        // 可信来源过滤，并与本地结果合并去重。
        var trusted = webResults.Where(IsTrustedSource).ToList();
        var dedup = trusted
            .Concat(local)
            .GroupBy(r => $"{r.Link}|{r.Name}|{r.Value}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (dedup.Count == 0)
        {
            return new List<TextSearchResult>
            {
                new("未检索到相关资料（网络与本地均未命中）。请更换关键词或稍后重试。") { Name = "未检索到相关资料" }
            };
        }

        var reranked = await _reranker.RerankAsync(query, dedup, cancellationToken);
        return reranked.Take(top).ToList();
    }

    private static bool IsTrustedSource(TextSearchResult r)
    {
        var uri = r.Link ?? string.Empty;
        if (string.IsNullOrWhiteSpace(uri)) return false;

        string[] trusted = [
            "reuters.com", "bloomberg.com", "ft.com", "wsj.com",
            "csrc.gov.cn", "sse.com.cn", "szse.cn", "sec.gov", "nasdaq.com",
            "imf.org", "worldbank.org", "ecb.europa.eu"
        ];
        return trusted.Any(d => uri.Contains(d, StringComparison.OrdinalIgnoreCase));
    }
}
