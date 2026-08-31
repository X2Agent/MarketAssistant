using System.Collections.Concurrent;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 告警交易联动门：查询某标的是否存在触发中的确认级告警
/// （TradingImpact = RequireConfirmation）。命中时 TradeExecutor 强制走人工确认。
/// </summary>
public interface IAlertGate
{
    bool IsGated(MarketType marketType, string symbol);
}

/// <summary>
/// 基于内存计数器的联动门实现：同一标的可叠加多条触发中的确认级告警，
/// 由 AlertCenterService 在告警触发/解除时增减计数。重启后内存态清零可接受
/// （价格告警条件仍成立时会很快重新触发）。
/// </summary>
public sealed class AlertGate : IAlertGate
{
    private readonly ConcurrentDictionary<GateKey, int> _activeCount = new();

    /// <summary>登记一条触发中的确认级告警。</summary>
    public void Activate(MarketType marketType, string symbol)
    {
        _activeCount.AddOrUpdate(
            new GateKey(marketType, AlertEvent.NormalizeSymbol(symbol)),
            1,
            (_, count) => count + 1);
    }

    /// <summary>解除一条确认级告警（计数归零后移除键，防止字典无限增长）。</summary>
    public void Deactivate(MarketType marketType, string symbol)
    {
        var key = new GateKey(marketType, AlertEvent.NormalizeSymbol(symbol));
        while (true)
        {
            if (!_activeCount.TryGetValue(key, out var count))
                return;

            if (count <= 1)
            {
                if (((ICollection<KeyValuePair<GateKey, int>>)_activeCount).Remove(new KeyValuePair<GateKey, int>(key, count)))
                    return;
                continue;
            }

            if (_activeCount.TryUpdate(key, count - 1, count))
                return;
        }
    }

    /// <inheritdoc />
    public bool IsGated(MarketType marketType, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return false;

        return _activeCount.TryGetValue(
            new GateKey(marketType, AlertEvent.NormalizeSymbol(symbol)),
            out var count) && count > 0;
    }

    private readonly record struct GateKey(MarketType MarketType, string Symbol);
}