namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 新浪财经个股资金流排行条目。
/// </summary>
/// <param name="Symbol">带市场前缀的代码（如 sh600519）。</param>
/// <param name="Name">股票名称。</param>
/// <param name="Price">最新价（字符串原值，可能为空）。</param>
/// <param name="ChangeRatio">涨跌幅（小数比率，如 -0.0082 表示 -0.82%）。</param>
/// <param name="NetAmount">净流入金额（元）。</param>
public sealed record SinaFundFlowItem(
    string Symbol,
    string Name,
    string Price,
    double ChangeRatio,
    double NetAmount);
