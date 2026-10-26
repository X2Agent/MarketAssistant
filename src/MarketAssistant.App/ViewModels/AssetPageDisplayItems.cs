using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 行情数据指标项（换手率 / 量比 / 市盈率 / 市净率）。Value 为空表示数据源未提供该指标，界面按 <see cref="HasValue"/> 隐藏该项。
/// </summary>
public sealed record MarketMetricItem(string Label, string Value)
{
    public bool HasValue => !string.IsNullOrEmpty(Value);
}

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
