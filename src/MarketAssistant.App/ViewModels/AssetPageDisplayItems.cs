using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 行情速览指标项。Value 为空表示数据源未提供该指标，界面按 <see cref="HasValue"/> 隐藏该项。
/// </summary>
public sealed record MarketMetricItem(string Label, string Value)
{
    public bool HasValue => !string.IsNullOrEmpty(Value);
}

/// <summary>技术指标项（MA / MACD / BOLL / KDJ 统一以「标签 + 数值」呈现）。</summary>
public sealed record IndicatorItem(string Label, string Value);

/// <summary>技术指标分组，例如「均线 / MACD / BOLL / KDJ」。</summary>
public sealed record IndicatorGroupItem(string Title, ObservableCollection<IndicatorItem> Items);

/// <summary>
/// 盘口单档。<see cref="ToneValue"/> 供 PriceChangeColorConverter 消费：
/// 正数取涨色（买盘），负数取跌色（卖盘）。
/// </summary>
public sealed record OrderBookLevelItem(
    string LevelLabel,
    string PriceText,
    string QuantityText,
    string AmountText,
    double DepthPercent,
    decimal ToneValue);

/// <summary>
/// 逐笔成交项。<see cref="ToneValue"/> 正数表示主动买入，负数表示主动卖出。
/// </summary>
public sealed record TickTradeItem(
    string TimeText,
    string PriceText,
    string QuantityText,
    string AmountText,
    decimal ToneValue);
