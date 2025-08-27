using MarketAssistant.Infrastructure;
using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// RAG ����������������˵��˵ļ������̡�
/// ���裺
/// 1) ��ѯ��д������ԭʼ��ѯ���ɶ����ѡ�������ٻء�
/// 2) ������������ÿ����ѡ���ڲ����ϼ������ϲ������
/// 3) ��ѡ���ںϣ��԰���ͼƬ������ı����ƶ� + ͼ�����ƶȡ���Ȩ�ںϴ�֡�
/// 4) ȥ�������ţ�ȥ��������Ŀ���������ŷ���õ���������
/// 5) ��ѡ Web ���䣺����Ҫʱ�����ⲿ������Դ���������
/// </summary>
public class RetrievalOrchestrator
{
    private readonly IQueryRewriteService _queryRewrite;
    private readonly IRerankerService _reranker;
    private readonly VectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IWebTextSearchFactory _webTextSearchFactory;
    private readonly ILogger<RetrievalOrchestrator> _logger;

    // ���ںϵ�Ȩ�����ã�
    // �ı�ռ�� 0.7��ͼ��ռ�� 0.3���ڶ�ģ̬��Ŀ����ʱ���Ƚ�����
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
    /// ���ڲ��������м������ѯ��صĶ��䡣
    /// </summary>
    /// <param name="query">�û���ѯ�ı���</param>
    /// <param name="collectionName">�����������ơ�</param>
    /// <param name="top">���ź󷵻ص�������</param>
    /// <param name="enableLateFusion">�Ƿ������ı�/ͼ�����ƶȵ����ںϡ�</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>����������ź�ļ��������</returns>
    public async Task<IReadOnlyList<TextSearchResult>> RetrieveAsync(
        string query,
        string collectionName,
        int top = 8,
        bool enableLateFusion = true,
        CancellationToken cancellationToken = default)
    {
        var collection = _vectorStore.GetCollection<string, TextParagraph>(collectionName);
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        // 1) ���ѯ��д������ԭʼ��ѯ���д��ѡ���������ٻء�
        var rewrites = await _queryRewrite.RewriteAsync(query, maxCandidates: 4);
        var queries = new List<string> { query };
        queries.AddRange(rewrites);

        // 2) ���������������ÿ����ѯ������м��������ϲ������
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

        // 3) ���ؼ���Ϊ�յĶ�����ʾ��
        if (merged.Count == 0)
        {
            return new List<TextSearchResult>
            {
                new("δ������������ϡ�������ؼ��ʻ���֪ʶ���Ƿ��������ȡ��") { Name = "δ�������������" }
            };
        }

        // 4) ��ѡ�����ںϡ�������Ŀ����ͼ���������򽫡��ı����ƶȡ��͡�ͼ�����ƶȡ���Ȩ�ںϡ�
        //    ͨ��������ʿ�ѡ���ԣ����ݲ�ͬ SDK �汾�µ��ֶα仯��
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

        // 5) ȥ�أ��� link+name+snippet ��Ϊ���������ظ���Ŀ��
        var dedup = merged
            .GroupBy(r => $"{r.Link}|{r.Name}|{r.Value}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        // 6) ������ضϣ��������ŷ���õ����������ȡǰ N ����
        var reranked = await _reranker.RerankAsync(query, dedup, cancellationToken);
        return reranked.Take(top).ToList();
    }

    /// <summary>
    /// ���Դӽ���Ŀ�ѡ Properties �ֶζ�ȡ Embedding<float>����������������
    /// ��ȱʧ�����Ͳ�ƥ�䣬���ؿ����顣
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
    /// ͨ�����䰲ȫ��ȡ�ɿ� double ���ԣ��� Score����
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
    /// ͨ�����䰲ȫ���ÿɿ� double ���ԣ��� Score����
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
    /// �ȴӱ���������������ٰ��貢��ɸѡ��� Web �����
    /// </summary>
    /// <param name="query">�û���ѯ�ı���</param>
    /// <param name="collectionName">�����������ơ�</param>
    /// <param name="top">���ź󷵻ص�������</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    public async Task<IReadOnlyList<TextSearchResult>> RetrieveWithWebAsync(
        string query,
        string collectionName,
        int top = 8,
        CancellationToken cancellationToken = default)
    {
        var local = await RetrieveAsync(query, collectionName, top, enableLateFusion: true, cancellationToken);
        if (local.Count == 1 && local[0].Name == "δ�������������")
        {
            // ���� fallback �ı������������� Web ���
        }

        // ����������� Web ����
        var textSearch = _webTextSearchFactory.Create();
        if (textSearch is null)
        {
            return local;
        }

        // �뱾�ؼ�������һ�£����ڸ�д��ѡ���ж��ѯ Web ������
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

        // ������Դ���ˣ����뱾�ؽ���ϲ�ȥ�ء�
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
                new("δ������������ϣ������뱾�ؾ�δ���У���������ؼ��ʻ��Ժ����ԡ�") { Name = "δ�������������" }
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
