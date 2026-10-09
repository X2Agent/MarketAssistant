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

/// <summary>
/// 交易环境模式。表达"在哪种环境、以何种市场形态交易"的语义，
/// 与具体交易所无关——交易所品牌由展示层（如 <c>TradingEnvironmentService</c>）负责拼接。
/// </summary>
/// <remarks>
/// <para>
/// <b>取值不可变更：</b>本枚举以数值形式持久化到 <c>UserSetting.json</c>
/// 与交易凭证加密文件，改名不影响数值，但<b>不得调整已有数值</b>
/// （1 为历史遗留空洞，保留不复用）。
/// </para>
/// <para>
/// 新增市场时按同一语义轴扩展成员（如 A 股的模拟盘），不要引入交易所品牌到成员名中。
/// </para>
/// </remarks>
public enum TradingMode
{
    /// <summary>实盘现货（真实资金，真实成交）</summary>
    [Description("实盘现货")]
    LiveSpot = 0,

    /// <summary>实盘合约（真实资金，支持杠杆与双向持仓）</summary>
    [Description("实盘合约")]
    LiveFutures = 2,

    /// <summary>合约测试网（虚拟资金，用于合约策略验证）</summary>
    [Description("合约测试网")]
    FuturesTestnet = 3,

    /// <summary>现货 Demo（虚拟余额，用于现货策略验证）</summary>
    [Description("现货 Demo")]
    SpotDemo = 4
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
