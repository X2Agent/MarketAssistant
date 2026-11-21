using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading;

/// <summary>实盘准入判定结果。</summary>
/// <param name="Allowed">是否允许该策略进入实盘。</param>
/// <param name="Reason">不允许时的可读原因（写入日志与告警）。</param>
/// <param name="SampleSize">已使用的影子样本数。</param>
public sealed record LivePromotionDecision(bool Allowed, string Reason, int SampleSize);

/// <summary>
/// 实盘准入闸门：只有影子模式已验证过的策略才允许在实盘环境自动下单。
///
/// 这不是"保证盈利"的机制，而是"阻止未经验证的策略直接花真钱"的机制。
/// 纯静态判定、无 IO，便于脱离交易所单测；阈值刻意保守，宁可多跑影子
/// 也不要让参数随手填的策略直接上实盘。
///
/// 判定条件（全部满足才放行）：
/// 1. 影子平仓样本 ≥ <see cref="MinimumShadowTrades"/>：样本太少时胜率是噪声放大器。
/// 2. 影子累计已实现盈亏 &gt; 0：连手续费都没赚回来的策略不该上实盘。
/// 3. 影子盈亏比 ≥ <see cref="MinimumProfitFactor"/>（无亏损交易视为通过）。
/// 4. 影子最大回撤占累计盈利的比例不超过 <see cref="MaximumDrawdownToProfitRatio"/>：
///    防止"高胜率但一把亏光"的形态通过胜率检查。
/// </summary>
public static class LivePromotionGate
{
    /// <summary>最少影子平仓样本。低于该值时胜率的置信区间宽到没有决策价值。</summary>
    public const int MinimumShadowTrades = 30;

    /// <summary>最低盈亏比。1.0 表示刚好盈亏平衡，留一点余量吸收滑点。</summary>
    public const decimal MinimumProfitFactor = 1.1m;

    /// <summary>最大回撤 / 累计盈利 的上限。超过说明盈利曲线极不稳定。</summary>
    public const decimal MaximumDrawdownToProfitRatio = 1.0m;

    /// <summary>
    /// 判定一条策略能否在实盘自动交易。
    /// </summary>
    /// <param name="shadow">该策略的影子绩效（无样本时 ClosedTrades 为 0）。</param>
    /// <param name="shadowMode">该策略当前是否仍处于影子模式。</param>
    public static LivePromotionDecision Evaluate(ShadowPerformance shadow, bool shadowMode)
    {
        // 影子策略本来就不下单，准入判定对它没有意义（它正是"还没毕业"的状态）
        if (shadowMode)
            return new LivePromotionDecision(false, "策略处于影子模式，不会向交易所下单", shadow.ClosedTrades);

        if (shadow.ClosedTrades < MinimumShadowTrades)
        {
            return new LivePromotionDecision(
                false,
                $"影子平仓样本 {shadow.ClosedTrades} 笔，少于实盘准入要求的 {MinimumShadowTrades} 笔，请继续影子验证",
                shadow.ClosedTrades);
        }

        if (shadow.TotalPnl <= 0)
        {
            return new LivePromotionDecision(
                false,
                $"影子累计盈亏 {shadow.TotalPnl:+0.00;-0.00;0.00} USDT 未转正，不允许进入实盘",
                shadow.ClosedTrades);
        }

        if (shadow.ProfitFactor.HasValue && shadow.ProfitFactor.Value < MinimumProfitFactor)
        {
            return new LivePromotionDecision(
                false,
                $"影子盈亏比 {shadow.ProfitFactor.Value:F2} 低于门槛 {MinimumProfitFactor:F2}，不允许进入实盘",
                shadow.ClosedTrades);
        }

        var drawdownRatio = shadow.MaxDrawdown / shadow.TotalPnl;
        if (drawdownRatio > MaximumDrawdownToProfitRatio)
        {
            return new LivePromotionDecision(
                false,
                $"影子最大回撤 {shadow.MaxDrawdown:F2} USDT 已达累计盈利的 {drawdownRatio:P0}，"
                + "超过上限，盈亏形态不稳定，不允许进入实盘",
                shadow.ClosedTrades);
        }

        return new LivePromotionDecision(
            true,
            $"影子验证通过：{shadow.ClosedTrades} 笔平仓，胜率 {shadow.WinRate:F1}%，"
            + $"累计 {shadow.TotalPnl:+0.00;-0.00;0.00} USDT，回撤 {shadow.MaxDrawdown:F2} USDT",
            shadow.ClosedTrades);
    }

    /// <summary>
    /// 仅按策略类型判断是否属于"需要准入验证"的自动交易策略。
    /// 目前只有 AI 信号策略具备影子模式（<c>ShadowTradingService</c> 只被 AI 执行器记账），
    /// 定投/网格没有影子绩效可验证——把它们纳入闸门只会让这两类策略在开启闸门后永远无法开仓。
    /// 待定投/网格具备影子路径后再纳入。
    /// 用户手工创建的一次性止损/止盈不属于策略选股逻辑，不做准入限制。
    /// </summary>
    public static bool RequiresPromotion(StrategyType type) =>
        type is StrategyType.AISignal;
}
