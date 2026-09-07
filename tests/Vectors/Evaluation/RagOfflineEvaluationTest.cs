using MarketAssistant.Rag;

namespace TestMarketAssistant.Vectors.Evaluation;

/// <summary>
/// P1-04 RAG 离线评估最小集（纯内存余弦，不依赖 API 密钥与向量库后端）：
/// 用确定性哈希假嵌入验证"距离语义 + 检索夹具阈值"，作为策略改动的可重复尺子。
/// 真实嵌入模型 + SQLiteVec 后端的端到端方向由带 JINA_API_KEY 的集成测试覆盖。
/// </summary>
[TestClass]
public class RagOfflineEvaluationTest
{
    /// <summary>确定性假嵌入：词袋符号哈希到 1024 维并归一化。相同文本必得相同向量。</summary>
    private static float[] Embed(string input)
    {
        const int dim = 1024;
        var vec = new float[dim];
        var tokens = input.ToLowerInvariant()
            .Split([' ', ',', '，', '。', '、', ';', '；', ':', '：', '?', '？', '!'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
            var index = BitConverter.ToUInt32(hash, 0) % dim;
            var sign = (hash[4] & 1) == 0 ? 1f : -1f;
            vec[index] += sign;
        }

        var norm = MathF.Sqrt(vec.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < dim; i++) vec[i] /= norm;
        }

        return vec;
    }

    private static float CosineDistance(float[] a, float[] b)
    {
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        // CosineDistance = 1 - cos(θ)，越小越相关
        return 1f - dot / (MathF.Sqrt(na) * MathF.Sqrt(nb) + 1e-9f);
    }

    [TestMethod]
    [TestCategory("Evaluation")]
    public void DistanceSemantics_RelevantDoc_ShouldHaveSmallestDistance()
    {
        var docs = new (string Key, string Text)[]
        {
            ("relevant", "贵州茅台 白酒龙头 毛利率91% 净利润747亿 高端白酒品牌护城河"),
            ("partial",  "食品饮料行业 消费板块 估值处于历史中位数附近 分化明显"),
            ("random",   "锂电上游碳酸锂价格波动 新能源车销量环比增长 数据仅供参考"),
        };

        var query = Embed("贵州茅台 白酒 净利润");
        var ranked = docs
            .Select(d => (d.Key, Distance: CosineDistance(query, Embed(d.Text))))
            .OrderBy(r => r.Distance)
            .ToList();

        Assert.AreEqual("relevant", ranked[0].Key,
            $"距离最小的应为相关文档，实际顺序: {string.Join(", ", ranked.Select(r => r.Key + ":" + r.Distance.ToString("F3")))}");

        Console.WriteLine("RAG 离线评估（距离语义）: " +
            string.Join(", ", ranked.Select(r => $"{r.Key}={r.Distance:F3}")));
    }

    [TestMethod]
    [TestCategory("Evaluation")]
    public void RecallAt1_OverFixtureSet_ShouldMeetThreshold()
    {
        var corpus = new (string Key, string Text)[]
        {
            ("maotai-fin",   "贵州茅台 年报 净利润747亿 毛利率91% 预收款项 充足现金流"),
            ("maotai-price", "贵州茅台 股价 收盘价1700元 成交额 市值2.1万亿 换手率"),
            ("catl-tech",    "宁德时代 动力电池装机量 市占率37% 麒麟电池量产 技术领先"),
            ("btc-market",   "比特币 BTC 现货ETF资金净流入 价格突破64000美元 资金费率"),
            ("macro-cpi",    "统计局公布 CPI同比上涨0.3% PPI降幅收窄 居民消费价格"),
            ("unrelated",    "天气预报 明日多云转晴 气温22度 适宜出行"),
        };

        var fixtures = new (string Query, string ExpectedTop1)[]
        {
            ("茅台 净利润 毛利率", "maotai-fin"),
            ("贵州茅台 股价 收盘", "maotai-price"),
            ("宁德时代 电池 市占率", "catl-tech"),
            ("比特币 ETF 资金费率", "btc-market"),
            ("CPI 同比 统计局", "macro-cpi"),
        };

        var embedded = corpus
            .Select(c => (c.Key, Vec: Embed(c.Text)))
            .ToList();

        var hits = 0;
        var failures = new List<string>();
        foreach (var (query, expected) in fixtures)
        {
            var qv = Embed(query);
            var top = embedded
                .Select(c => (c.Key, Distance: CosineDistance(qv, c.Vec)))
                .OrderBy(r => r.Distance)
                .First();

            if (top.Key == expected) hits++;
            else failures.Add($"query='{query}' expected='{expected}' got='{top.Key}'");
        }

        var recall = (double)hits / fixtures.Length;
        Console.WriteLine($"RAG 离线评估 Recall@1: {recall:P0} ({hits}/{fixtures.Length})" +
            (failures.Count > 0 ? $" 未命中: {string.Join("; ", failures)}" : ""));

        Assert.IsTrue(recall >= 0.8,
            $"Recall@1 应 ≥ 80%，实际 {recall:P0}。未命中: {string.Join("; ", failures)}");
    }
}