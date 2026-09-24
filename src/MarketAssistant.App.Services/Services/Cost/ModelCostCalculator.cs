using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Applications.Settings;

namespace MarketAssistant.Services.Cost;

/// <summary>
/// 单个模型的成本明细行。<see cref="IsPriced"/> 为 false 表示该条目未匹配到价格，
/// 其 Token 消耗计入 <see cref="ModelCostEstimate.HasUnpriced"/> 但不计入总金额。
/// </summary>
public sealed record ModelCostLine(
    string AgentName,
    string ModelId,
    long InputTokens,
    long OutputTokens,
    decimal Cost,
    bool IsPriced);

/// <summary>
/// 一次分析的 Token 成本估算结果。
/// <see cref="HasUnpriced"/> 为 true 时 <see cref="TotalCost"/> 只是已定价部分的下界。
/// </summary>
public sealed record ModelCostEstimate(
    decimal TotalCost,
    bool HasUnpriced,
    IReadOnlyList<ModelCostLine> Lines)
{
    public bool HasValue => Lines.Count > 0;
}

/// <summary>
/// Token 成本估算纯逻辑：单价按元 / 1M tokens 计。
/// 无价格表时全部行标记为未定价，UI 据此隐去金额而不是展示错误的 0。
/// </summary>
public static class ModelCostCalculator
{
    private const decimal TokensPerMillion = 1_000_000m;

    /// <summary>
    /// 估算成本。<paramref name="modelResolver"/> 按 Agent 名解析本次实际使用的模型，
    /// 返回 "provider|model" 或纯模型 ID；返回 null 表示无法确定（按未定价处理，不猜默认价）。
    /// </summary>
    public static ModelCostEstimate Estimate(
        AnalysisTokenUsage? usage,
        IReadOnlyList<ModelPrice> prices,
        Func<string, string?> modelResolver)
    {
        ArgumentNullException.ThrowIfNull(modelResolver);

        if (usage is null || !usage.HasValue)
        {
            return new ModelCostEstimate(0m, false, []);
        }

        var priceLookup = new Dictionary<(string, string), ModelPrice>();
        foreach (var price in prices)
        {
            if (price.IsMatchable)
            {
                priceLookup[(price.ProviderId, price.ModelId)] = price;
            }
        }

        var lines = new List<ModelCostLine>();
        var total = 0m;
        var hasUnpriced = false;

        foreach (var entry in usage.Entries)
        {
            var modelKey = modelResolver(entry.AgentName);
            var matched = modelKey is null ? null : FindPrice(priceLookup, modelKey);

            if (matched is null)
            {
                hasUnpriced = true;
                lines.Add(new ModelCostLine(
                    entry.AgentName, modelKey ?? string.Empty, entry.InputTokens, entry.OutputTokens, 0m, false));
                continue;
            }

            var cost = entry.InputTokens / TokensPerMillion * matched.InputPricePerMillionTokens
                       + entry.OutputTokens / TokensPerMillion * matched.OutputPricePerMillionTokens;
            total += cost;
            lines.Add(new ModelCostLine(
                entry.AgentName, matched.ModelId, entry.InputTokens, entry.OutputTokens, cost, true));
        }

        return new ModelCostEstimate(total, hasUnpriced, lines);
    }

    /// <summary>
    /// 价格匹配：模型键含 "provider|model" 前缀时精确匹配；
    /// 否则按模型 ID 匹配（不同 provider 同名模型取第一个，接受该歧义）。
    /// </summary>
    private static ModelPrice? FindPrice(
        IReadOnlyDictionary<(string, string), ModelPrice> priceLookup,
        string modelKey)
    {
        var separatorIndex = modelKey.IndexOf('|');
        if (separatorIndex > 0 && separatorIndex < modelKey.Length - 1)
        {
            var provider = modelKey[..separatorIndex];
            var model = modelKey[(separatorIndex + 1)..];
            if (priceLookup.TryGetValue((provider, model), out var exact))
            {
                return exact;
            }
        }

        foreach (var entry in priceLookup)
        {
            if (string.Equals(entry.Key.Item2, modelKey, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// 成本展示文本：全定价输出"约 ¥0.42"；存在未定价模型时输出"≥ ¥0.42（部分未定价）"；
    /// 无任何已定价消耗时返回空串（调用方应隐去金额）。
    /// </summary>
    public static string FormatCost(ModelCostEstimate estimate)
    {
        if (!estimate.HasValue || estimate.TotalCost <= 0m)
        {
            return string.Empty;
        }

        var amount = estimate.TotalCost < 0.01m
            ? estimate.TotalCost.ToString("0.0000")
            : estimate.TotalCost.ToString("0.00");
        return estimate.HasUnpriced ? $"≥ ¥{amount}（部分未定价）" : $"约 ¥{amount}";
    }
}
