using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Data;
using MarketAssistant.Vectors.Interfaces;
using System.Text.RegularExpressions;

namespace MarketAssistant.Vectors.Services;

public class RerankerService : IRerankerService
{
    private readonly ILogger<RerankerService> _logger;
    private readonly IChatCompletionService? _chat;

    public RerankerService(ILogger<RerankerService> logger, IServiceProvider sp)
    {
        _logger = logger;
        _chat = sp.GetService<IChatCompletionService>();
    }

    public async Task<IReadOnlyList<TextSearchResult>> RerankAsync(string query, IEnumerable<TextSearchResult> items, CancellationToken cancellationToken = default)
    {
        var list = items.ToList();
        if (list.Count == 0) return list;
        try
        {
            if (_chat != null && list.Count <= 12)
            {
                var prompt = BuildScorePrompt(query, list);
                var resp = await _chat.GetChatMessageContentAsync(new ChatHistory(prompt), cancellationToken: cancellationToken);
                var scores = TryParseScores(resp?.Content);
                if (scores != null && scores.Count == list.Count)
                {
                    var paired = list.Zip(scores, (item, s) => (item, s));
                    return paired.OrderByDescending(p => p.s).Select(p => p.item).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cross-encoder scoring failed, fallback to heuristic");
        }
        return HeuristicRerank(query, list);
    }

    private static string BuildScorePrompt(string query, List<TextSearchResult> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("请作为检索重排模型，对下面候选片段与查询的相关性打分（0-100，越高越相关）。仅输出逗号分隔的数字序列，不要解释。");
        sb.AppendLine($"查询: {query}");
        for (int i = 0; i < items.Count; i++)
        {
            var v = items[i].Value ?? items[i].Name ?? string.Empty;
            sb.AppendLine($"[{i+1}] {v}");
        }
        sb.AppendLine("输出: 例如 80,65,12");
        return sb.ToString();
    }

    private static List<double>? TryParseScores(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var parts = content.Split(new[] { ',', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p.Replace("%", string.Empty), out var v)) list.Add(v);
        }
        return list.Count > 0 ? list : null;
    }

    private IReadOnlyList<TextSearchResult> HeuristicRerank(string query, List<TextSearchResult> items)
    {
        var qTokens = Tokenize(query);
        var scored = new List<(TextSearchResult Item, double Score)>();
        foreach (var r in items)
        {
            var text = (r.Value ?? string.Empty) + " " + (r.Name ?? string.Empty);
            var tTokens = Tokenize(text);
            var overlap = qTokens.Intersect(tTokens, StringComparer.OrdinalIgnoreCase).Count();
            var sim = qTokens.Count > 0 ? (double)overlap / qTokens.Count : 0.0;
            var trust = GetTrustBonus(r);
            var lenPenalty = Math.Min(text.Length, 2000) / 2000.0;
            var score = 0.6 * sim + 0.3 * trust + 0.1 * (1 - lenPenalty);
            scored.Add((r, score));
        }
        return scored.OrderByDescending(s => s.Score).Select(s => s.Item).ToList();
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"[^\p{L}\p{N}]+", " ");
        return new HashSet<string>(text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
    }

    private static double GetTrustBonus(TextSearchResult r)
    {
        var uri = r.Link ?? string.Empty;
        if (string.IsNullOrWhiteSpace(uri)) return 0.4;

        string[] trusted = [
            "gov.cn", "pbc.gov.cn", "csrc.gov.cn", "cbirc.gov.cn", "sse.com.cn", "szse.cn",
            "cffex.com.cn", "dce.com.cn", "czce.com.cn", "shfe.com.cn",
            "xinhuanet.com", "people.com.cn", "cctv.com", "cnstock.com", "cs.com.cn",
            "stcn.com", "yicai.com", "caixin.com", "eastmoney.com", "finance.sina.com.cn",
            "jrj.com.cn", "cnfol.com"
        ];
        string[] caution = [
            "weibo.com", "x.com", "twitter.com", "tieba.baidu.com", "zhihu.com",
            "douban.com", "bilibili.com", "xueqiu.com", "toutiao.com", "baijiahao.baidu.com"
        ];

        if (trusted.Any(d => uri.Contains(d, StringComparison.OrdinalIgnoreCase))) return 1.0;
        if (caution.Any(d => uri.Contains(d, StringComparison.OrdinalIgnoreCase))) return 0.2;
        return 0.6;
    }
}
