namespace MarketAssistant.Services.Market;

/// <summary>
/// 行情条指标的趋势语义，决定顶栏涨跌幅文本的配色。
/// </summary>
public enum TickerTrend
{
    /// <summary>上涨（红涨）。A 股语义：涨为红。</summary>
    Up,

    /// <summary>下跌（绿跌）。</summary>
    Down,

    /// <summary>平盘（涨跌为 0，使用中性色）。</summary>
    Flat,

    /// <summary>该指标无涨跌语义（如恐惧贪婪指数、主导率），仅展示数值。</summary>
    None,
}

/// <summary>
/// 顶栏行情条的单个市场级指标项。所有展示字符串均已在服务侧格式化完成，
/// 视图层保持纯展示，不参与数值解析或本地化。
/// </summary>
/// <param name="Name">指标名称（如"上证指数"、"总市值"、"恐惧贪婪"）。</param>
/// <param name="Value">已格式化的数值文本（如"3,094.67"、"$2.4T"、"52.3%"、"44"）。</param>
/// <param name="ChangeText">已格式化的涨跌/分类文本（如"+1.82%"、"恐惧"）；无则为 null。</param>
/// <param name="Trend">涨跌语义，决定 ChangeText 配色。</param>
public sealed record IndexQuoteItem(
    string Name,
    string Value,
    string? ChangeText,
    TickerTrend Trend);

/// <summary>
/// 市场级聚合指标（行情条）拉取抽象：返回当前市场的顶层指数/情绪指标，
/// 而非具体标的价格。各市场实现负责取数与格式化，消费方（顶栏）纯展示。
/// </summary>
public interface IIndexQuoteService
{
    /// <summary>
    /// 拉取当前市场的行情条指标集合。实现内部应吞掉可恢复的局部失败（降级为部分项），
    /// 仅在完全无数据时返回空集合（顶栏随之整体隐藏）。
    /// </summary>
    Task<IReadOnlyList<IndexQuoteItem>> GetLatestAsync(CancellationToken cancellationToken = default);
}
