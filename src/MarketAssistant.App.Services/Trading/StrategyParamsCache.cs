using System.Collections.Concurrent;
using System.Text.Json;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 策略 CustomParams 解析结果缓存。
/// 价格 tick 约每秒一次，Grid/DCA/AISignal/TrailingStop 逐 tick 反序列化参数是纯 CPU 开销；
/// 参数仅在策略编辑时变化（策略变更广播时整体 <see cref="Clear"/>），
/// 运行时自更新参数的路径（网格基准/触发、DCA 加倍）经 <see cref="Refresh{T}"/> 同步刷新条目。
/// </summary>
/// <remarks>
/// 键为 (策略 ID, 参数类型)：同一策略的 TrailingStop 参数与 RiskProfile 解析互不冲突；
/// Raw 与当前串不一致视为过期重新解析，兜底直写 DB 等旁路变更。
/// </remarks>
internal sealed class StrategyParamsCache
{
    private readonly ConcurrentDictionary<(string StrategyId, Type ParamsType), (string Raw, object Parsed)> _cache = new();

    /// <summary>
    /// 解析并缓存：缓存命中（原始串一致）直接复用解析对象。
    /// 解析异常向调用方传播（由各评估方法按自己的 try/catch 记日志），失败结果不进缓存。
    /// </summary>
    public TParsed? GetOrCreate<TParsed>(string strategyId, string? raw, Func<string, TParsed?> parse)
        where TParsed : class
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        var key = (strategyId, typeof(TParsed));
        if (_cache.TryGetValue(key, out var entry) && entry.Raw == raw)
            return (TParsed)entry.Parsed;

        var parsed = parse(raw);
        if (parsed != null)
            _cache[key] = (raw, parsed);
        return parsed;
    }

    /// <summary>运行时自更新参数后同步刷新条目，避免下个 tick 因串不一致重复解析。</summary>
    public void Refresh<TParsed>(string strategyId, string raw, TParsed parsed)
        where TParsed : class
        => _cache[(strategyId, typeof(TParsed))] = (raw, parsed);

    public void Clear() => _cache.Clear();
}

/// <summary>追踪止损策略的 CustomParams 解析结果（可选字段，运行期不变）。</summary>
internal sealed record TrailingStopParams(decimal? TrailingPercent, decimal? ActivationPrice);

/// <summary>
/// 策略参数解析器。静态委托避免逐次调用分配；
/// 失败语义与提取前一致：Grid/DCA 抛 JsonException 由调用方捕获，AISignal 执行器使用严格 FromJson；引擎等旁路链路在下方安全降级为 null。
/// </summary>
internal static class StrategyParamsParsers
{
    public static readonly Func<string, TrailingStopParams?> TrailingStop = static raw =>
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        decimal? trailingPercent = null;
        decimal? activationPrice = null;
        if (root.TryGetProperty("trailingPercent", out var trailingPercentEl)
            && trailingPercentEl.TryGetDecimal(out var parsedPercent))
            trailingPercent = parsedPercent;
        if (root.TryGetProperty("activationPrice", out var activationPriceEl)
            && activationPriceEl.TryGetDecimal(out var parsedActivation))
            activationPrice = parsedActivation;
        return new TrailingStopParams(trailingPercent, activationPrice);
    };

    public static readonly Func<string, GridTradingParams?> Grid =
        static raw => JsonSerializer.Deserialize<GridTradingParams>(raw);

    public static readonly Func<string, DCAParams?> Dca =
        static raw => JsonSerializer.Deserialize<DCAParams>(raw);

    public static readonly Func<string, AISignalParams?> AiSignal =
        static raw => AISignalParams.TryFromJson(raw, out var parameters) ? parameters : null;
}
