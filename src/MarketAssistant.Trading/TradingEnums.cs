using System.ComponentModel;

namespace MarketAssistant.Trading.Models;

/// <summary>
/// 策略类型
/// </summary>
public enum StrategyType
{
    StopLoss,
    TakeProfit,
    TrailingStop,
    GridTrading,
    DCA,
    AISignal
}

public enum StrategyStatus { Active, Paused, Completed, Failed }

public enum OrderSide { Buy, Sell }

public enum OrderType { Market, Limit }

public enum CryptoTradingMode
{
    [Description("Binance 实盘现货")]
    LiveSpot,

    [Description("Binance Spot Testnet")]
    BinanceTestnet,

    [Description("Binance 实盘合约")]
    LiveFutures,

    [Description("Binance Futures Testnet")]
    BinanceFuturesTestnet
}

public enum TradeRecordStatus { Pending, Filled, PartiallyFilled, Cancelled, Failed }

/// <summary>
/// 持仓方向（与 OrderSide 区分，用于 positions 表）
/// </summary>
public enum PositionSide { Long, Short }
