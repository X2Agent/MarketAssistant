namespace MarketAssistant.Trading.Models;

/// <summary>
/// 风控配置
/// </summary>
public class RiskConfig
{
    public decimal MaxSingleOrderPercent { get; set; } = 3;
    public decimal MaxDailyLossPercent { get; set; } = 5;
    public decimal MaxTotalPositionPercent { get; set; } = 70;
    public int MaxDailyTrades { get; set; } = 15;
    public decimal MinOrderAmount { get; set; } = 10;
    public bool RequireConfirmation { get; set; } = true;
    public decimal ConfirmationThreshold { get; set; } = 500;

    /// <summary>
    /// 最大回撤熔断。累计回撤超过此百分比时停止所有交易。
    /// </summary>
    public decimal MaxDrawdownPercent { get; set; } = 20;

    /// <summary>
    /// 单 symbol 最大仓位占比（0 表示不限制）。
    /// </summary>
    public decimal MaxSinglePositionPercent { get; set; } = 30;

    /// <summary>
    /// 合约持仓强平距离预警阈值（%）：标记价距强平价小于该值时发警告。
    /// </summary>
    public decimal LiquidationWarnPercent { get; set; } = 25;

    /// <summary>
    /// 合约持仓强平距离紧急阈值（%）：小于该值时发紧急告警，
    /// 并触发该标的的交易联动门（新开仓信号强制人工确认）。
    /// </summary>
    public decimal LiquidationCriticalPercent { get; set; } = 10;

    /// <summary>
    /// 合约开仓杠杆倍数（1-125）。硬编码会让用户无法按自身风险承受能力调整。
    /// </summary>
    public int FuturesLeverage { get; set; } = 10;

    /// <summary>
    /// 市价单成交滑点告警阈值（%）：成交价相对信号价偏离超过该值时告警。
    /// 市价单无法事前限制滑点，只能事后发现并提示。
    /// </summary>
    public decimal MaxSlippagePercent { get; set; } = 1;

    /// <summary>
    /// 策略限价单超时时间（秒）：挂起超过该时长未成交则撤单并告警，0 表示不自动撤单。
    /// </summary>
    public int LimitOrderTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 连续亏损熔断次数：当日实现亏损笔数连续达到该值时停止新开仓，0 表示不限制。
    /// </summary>
    public int MaxConsecutiveLosses { get; set; } = 5;

    /// <summary>
    /// 合约名义总敞口上限（占权益百分比）：Σ|持仓名义| + 本单名义不得超过该比例，0 表示不限制。
    /// 现货仓位占比无法反映杠杆放大后的真实风险，这是合约的等效约束。
    /// </summary>
    public decimal MaxFuturesNotionalPercent { get; set; } = 300;

    /// <summary>
    /// 交易所 API 失败熔断阈值（次）：滑动窗口内失败达到该值时拒单并告警，0 表示不限制。
    /// </summary>
    public int ApiFailureThreshold { get; set; } = 5;

    /// <summary>
    /// 资金费率护栏（仅合约开仓生效）：距下次资金结算少于该分钟数，
    /// 且费率对本次开仓方向不利时，推迟开仓。0 表示不启用。
    /// </summary>
    /// <remarks>
    /// 资金费每 8 小时结算一次，持仓跨结算点会被收费。正费率时多头付费、空头收费；
    /// 负费率相反。刚进场就跨结算点、且费率对己方不利，等于开局先亏一笔，
    /// 与入场时机无关但完全吃掉薄利策略的边际。建议开启后设 5 分钟：只挡"马上就要结算"的仓位，
    /// 不干扰正常交易节奏。默认 0（关闭）：这是新增的收紧性约束，存量用户不应在升级后被静默限单
    /// ——与 <see cref="RequireLivePromotion"/> 的默认策略一致。
    /// </remarks>
    public int FundingRateGuardMinutes { get; set; }

    /// <summary>
    /// 不利资金费率的容忍阈值（每 8 小时结算周期的费率百分比）：绝对值低于该值视为噪声，不拦单。
    /// 默认 0.03%（即万三），略高于常见的 0.01%~0.02% 基准费率。仅在 GuardMinutes 大于 0 时生效。
    /// </summary>
    public decimal FundingRateTolerancePercent { get; set; } = 0.03m;

    /// <summary>
    /// 最大同时持仓标的数（0 表示不限制）。
    /// 单标的仓位上限管不住"相关性"：5 个山寨币同时开多，实际等于一个 5 倍的 BTC 多头，
    /// 回撤会同步发生。限制持仓标的数是这条风险最易落地的代理约束。
    /// 默认 0（不限制）：与 <see cref="RequireLivePromotion"/> 同理，新增的收紧性约束
    /// 不应静默改变存量用户的交易行为，由用户显式开启。
    /// </summary>
    public int MaxConcurrentPositions { get; set; }

    /// <summary>
    /// 是否要求策略通过影子验证才允许在实盘自动开仓。
    /// 默认关闭——开启后未积累足够影子样本的已有策略会立即停止实盘交易，
    /// 这属于用户显式选择的风险策略收紧，不由程序代替用户决定默认值。
    /// 规则见 <c>LivePromotionGate</c>：≥30 笔影子平仓、累计盈亏转正、
    /// 盈亏比 ≥1.1、回撤不超过累计盈利。
    /// </summary>
    public bool RequireLivePromotion { get; set; }
}

/// <summary>
/// 风控检查结果
/// </summary>
public class RiskCheckResult
{
    public bool Passed { get; set; }
    public bool NeedsConfirmation { get; set; }
    public bool Deferred { get; init; }
    public string? Reason { get; set; }

    public static RiskCheckResult Pass() => new() { Passed = true };
    public static RiskCheckResult Reject(string reason) => new() { Passed = false, Reason = reason };
    public static RiskCheckResult RequireConfirmation(string reason) =>
        new() { Passed = false, NeedsConfirmation = true, Reason = reason };
}

/// <summary>
/// 日统计
/// </summary>
public class DailyStats
{
    public string Date { get; set; } = string.Empty;
    public int TradeCount { get; set; }
    public decimal TotalPnl { get; set; }
    public decimal TotalCommission { get; set; }

    /// <summary>
    /// 当日连续亏损笔数（实现盈亏为负累计，盈亏后归零）。用于连续亏损熔断。
    /// 开仓成交（无已实现盈亏）不参与计数，避免每笔开仓把连亏计数清零。
    /// </summary>
    public int ConsecutiveLosses { get; set; }
}
