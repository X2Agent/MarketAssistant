namespace MarketAssistant.Trading.Models;

/// <summary>
/// 网格交易策略参数
/// </summary>
public class GridTradingParams
{
    /// <summary>
    /// 网格上界价格
    /// </summary>
    public decimal UpperPrice { get; set; }

    /// <summary>
    /// 网格下界价格
    /// </summary>
    public decimal LowerPrice { get; set; }

    /// <summary>
    /// 网格数量（将在上界与下界之间均匀分布）
    /// </summary>
    public int GridCount { get; set; } = 5;

    /// <summary>
    /// 每格交易数量
    /// </summary>
    public decimal QuantityPerGrid { get; set; }

    /// <summary>
    /// 上次触发的网格价格索引（用于追踪状态）
    /// </summary>
    public int LastTriggeredIndex { get; set; } = -1;

    /// <summary>
    /// 破网止损价（可选）。价格跌破此值时清仓所有网格多头仓位。
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// 破网止盈价（可选）。价格涨破此值时清仓所有网格空头仓位。
    /// </summary>
    public decimal? TakeProfitPrice { get; set; }

    /// <summary>
    /// 计算网格间距
    /// </summary>
    public decimal GridSpacing => GridCount > 1 ? (UpperPrice - LowerPrice) / GridCount : 0;

    /// <summary>
    /// 获取指定索引处的网格价格
    /// </summary>
    public decimal GetGridPrice(int index) => LowerPrice + GridSpacing * index;
}

/// <summary>
/// 定投（DCA）策略参数
/// </summary>
public class DCAParams
{
    /// <summary>
    /// 定投间隔（秒）
    /// </summary>
    public int IntervalSeconds { get; set; } = 86400; // 默认每天

    /// <summary>
    /// 每次定投数量
    /// </summary>
    public decimal AmountPerInterval { get; set; }

    /// <summary>
    /// 价格上限（高于此价不买入，0 表示无限制）
    /// </summary>
    public decimal MaxBuyPrice { get; set; }

    /// <summary>
    /// 价格下限触发加倍（低于此价双倍买入，0 表示不启用）
    /// </summary>
    public decimal DoubleBuyBelowPrice { get; set; }

    /// <summary>
    /// 加倍冷却期（秒）。两次加倍之间至少间隔此时间，默认 24 小时。
    /// 防止瀑布式下跌中连续加倍耗尽资金。
    /// </summary>
    public int DoubleBuyCooldownSeconds { get; set; } = 86400;

    /// <summary>
    /// 加倍次数上限（0 表示不限制）。防止无限制加倍。
    /// </summary>
    public int MaxDoubleBuyCount { get; set; } = 3;

    /// <summary>
    /// 上次加倍时间（ISO 8601）。用于冷却期判断，持久化在 CustomParams 中。
    /// </summary>
    public string? LastDoubleBuyAt { get; set; }

    /// <summary>
    /// 已加倍次数。用于上限判断，持久化在 CustomParams 中。
    /// </summary>
    public int DoubleBuyCount { get; set; }
}
