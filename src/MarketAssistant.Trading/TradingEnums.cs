using System.ComponentModel;

namespace MarketAssistant.Trading.Models;

/// <summary>
/// 策略类型
/// </summary>
public enum StrategyType
{
    [Description("止损")]
    StopLoss,
    [Description("止盈")]
    TakeProfit,
    [Description("追踪止损")]
    TrailingStop,
    [Description("网格交易")]
    GridTrading,
    [Description("定投")]
    DCA,
    [Description("AI 信号")]
    AISignal
}

public enum StrategyStatus
{
    [Description("运行中")]
    Active,
    [Description("已暂停")]
    Paused,
    [Description("已完成")]
    Completed,
    [Description("失败")]
    Failed
}

public enum OrderSide
{
    [Description("买入")]
    Buy,
    [Description("卖出")]
    Sell
}

/// <summary>
/// 订单类型。Market/Limit 为基本类型；StopMarket/TakeProfitMarket/TrailingStopMarket 为合约条件单类型。
/// </summary>
public enum OrderType
{
    Market,
    Limit,
    /// <summary>止损市价单（合约专用，触发后以市价平仓）</summary>
    StopMarket,
    /// <summary>止盈市价单（合约专用，触发后以市价平仓）</summary>
    TakeProfitMarket,
    /// <summary>追踪止损市价单（合约专用，按回调比例触发）</summary>
    TrailingStopMarket
}

public enum CryptoTradingMode
{
    [Description("Binance 实盘现货")]
    LiveSpot = 0,

    [Description("Binance 实盘合约")]
    LiveFutures = 2,

    [Description("Binance Futures Testnet")]
    BinanceFuturesTestnet = 3,

    [Description("Binance 现货 Demo")]
    BinanceSpotDemo = 4
}

public enum TradeRecordStatus
{
    [Description("待成交")]
    Pending,
    [Description("已成交")]
    Filled,
    [Description("部分成交")]
    PartiallyFilled,
    [Description("已取消")]
    Cancelled,
    [Description("失败")]
    Failed
}

/// <summary>
/// 持仓方向（与 OrderSide 区分，用于 positions 表）
/// </summary>
public enum PositionSide
{
    [Description("多头")]
    Long,
    [Description("空头")]
    Short
}
