using System.Text;
using System.Text.Json;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Trading.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// AI 信号策略执行器：封装硬性边界处理、Prompt 构建、Agent 调用与成交回写识别。
/// </summary>
public sealed partial class AISignalStrategyExecutor(
    ITradingAgentFactory agentFactory,
    TradingDataService dataService,
    TradingStrategyService strategyService,
    CryptoPortfolioService portfolioService,
    AnalysisReportCache reportCache,
    TradeExecutor tradeExecutor,
    ShadowTradingService shadowTradingService,
    SignalAlertEvaluator signalAlertEvaluator,
    ILogger<AISignalStrategyExecutor> logger)
{
    private readonly ITradingAgentFactory _agentFactory = agentFactory;
    private readonly TradingDataService _dataService = dataService;
    private readonly TradingStrategyService _strategyService = strategyService;
    private readonly CryptoPortfolioService _portfolioService = portfolioService;
    private readonly AnalysisReportCache _reportCache = reportCache;
    private readonly TradeExecutor _tradeExecutor = tradeExecutor;
    private readonly ShadowTradingService _shadowTradingService = shadowTradingService;
    private readonly SignalAlertEvaluator _signalAlertEvaluator = signalAlertEvaluator;
    private readonly ILogger<AISignalStrategyExecutor> _logger = logger;

    public async Task<AISignalExecutionResult> ExecuteAsync(
        TradingStrategy strategy,
        decimal currentPrice,
        CancellationToken ct = default)
    {
        AISignalParams? aiParams;
        try
        {
            aiParams = AISignalParams.FromJson(strategy.CustomParams);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "AI 策略参数损坏，暂停策略: {StrategyId}", strategy.Id);
            return await PauseForInvalidConfigurationAsync(
                strategy, "AI 策略参数 JSON 损坏", ct).ConfigureAwait(false);
        }

        if (aiParams == null)
        {
            _logger.LogError("AI 策略缺少参数，暂停策略: {StrategyId}", strategy.Id);
            return await PauseForInvalidConfigurationAsync(
                strategy, "AI 策略缺少参数", ct).ConfigureAwait(false);
        }

        // 持仓方向以本地账本为准（按 StrategyId 精确归属），而不是 strategy.Side：
        // AI 决策方向驱动下单方向后，同一策略可以先开多再开空（合约），
        // strategy.Side 只是创建时的初始方向，不再可靠。无持仓记录时按 Side 兜底。
        var isLongPosition = await ResolveIsLongPositionAsync(strategy, ct).ConfigureAwait(false);

        if (TryHandleHardBoundary(strategy, currentPrice, isLongPosition, out var boundaryReasoning))
        {
            // 影子模式下硬性边界也只记账不下单：影子策略全程不得触达交易所
            if (aiParams.ShadowMode)
                return await RecordShadowExitAsync(strategy, currentPrice, boundaryReasoning, ct).ConfigureAwait(false);

            return await ExecuteHardBoundaryAsync(strategy, currentPrice, isLongPosition, boundaryReasoning, ct)
                .ConfigureAwait(false);
        }

        return await ExecuteWithAgentAsync(strategy, currentPrice, aiParams, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 配置无效属于确定性失败：暂停策略并告警一次，避免普通冷却后周期性重复执行。
    /// </summary>
    private async Task<AISignalExecutionResult> PauseForInvalidConfigurationAsync(
        TradingStrategy strategy, string reason, CancellationToken ct)
    {
        await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Paused, ct)
            .ConfigureAwait(false);
        await _signalAlertEvaluator.NotifyStrategyConfigurationPausedSafeAsync(strategy, reason)
            .ConfigureAwait(false);
        return AISignalExecutionResult.ConfigurationFailed(reason);
    }

    /// <summary>
    /// 从本地持仓账本解析该策略的实际持仓方向：有多头行 → 多头；有空头行 → 空头；
    /// 无记录（尚未成交或外部平仓）→ 按 strategy.Side 兜底。
    /// </summary>
    private async Task<bool> ResolveIsLongPositionAsync(TradingStrategy strategy, CancellationToken ct)
    {
        var openPositions = await _dataService.GetOpenPositionsAsync(strategy.Symbol, ct).ConfigureAwait(false);
        var own = openPositions
            .Where(p => string.Equals(p.StrategyId, strategy.Id, StringComparison.OrdinalIgnoreCase)
                        && p.RemainingQuantity > 0)
            .ToList();
        if (own.Count == 0)
            return strategy.Side == OrderSide.Buy;

        // 合约双向持仓下同策略理论上可同时有多空行，按数量大者为准（实际不可达，防御性取主方向）
        var longQty = own.Where(p => p.Side == PositionSide.Long).Sum(p => p.RemainingQuantity);
        var shortQty = own.Where(p => p.Side == PositionSide.Short).Sum(p => p.RemainingQuantity);
        return longQty >= shortQty;
    }

    /// <summary>
    /// 硬性边界（止损/止盈）处理：确认仍有持仓后反向平仓。
    /// 平仓成功或持仓已消失时完结策略，防止退出条件兑现后策略按评估间隔反复触发
    /// （TradeExecutor 仅在成功时回写触发计数，失败时保持 Active 留待下个冷却期重试）。
    /// </summary>
    private async Task<AISignalExecutionResult> ExecuteHardBoundaryAsync(
        TradingStrategy strategy,
        decimal currentPrice,
        bool isLongPosition,
        string reasoning,
        CancellationToken ct)
    {
        try
        {
            var positions = await _portfolioService.GetCurrentPositionsAsync(ct).ConfigureAwait(false);
            var hasPosition = positions.Any(position =>
                position.Symbol.Equals(strategy.Symbol, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(position.Quantity) > 0);

            if (!hasPosition)
            {
                // 持仓已不存在（如手动平仓），退出条件失去对象，策略使命结束
                _logger.LogInformation(
                    "硬性边界触发但已无持仓，直接完结策略: {StrategyId} {Symbol}",
                    strategy.Id, strategy.Symbol);
                await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, ct)
                    .ConfigureAwait(false);
                return AISignalExecutionResult.NoTrade;
            }

            // TradeExecutor 从 strategy 读取执行方向；用 try/finally 把方向翻转限制在本次调用内，
            // 避免"持仓方向"语义被平仓动作污染（当前策略对象每次评估重新加载，此处防御性恢复使约定显式化）。
            // 平仓方向由实际持仓方向推导：多头卖出、空头买入
            var originalSide = strategy.Side;
            try
            {
                strategy.Side = isLongPosition ? OrderSide.Sell : OrderSide.Buy;

                var boundaryResult = await _tradeExecutor.ExecuteTradeAsync(
                    strategy, currentPrice, reasoning,
                    requireClose: true,
                    ct: ct).ConfigureAwait(false);

                if (boundaryResult.Success)
                {
                    // 止损/止盈退出即本策略使命完成；重新建仓应通过新策略表达
                    _logger.LogInformation("硬性边界平仓成功，完结策略: {StrategyId}", strategy.Id);
                    await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, ct)
                        .ConfigureAwait(false);
                    return new AISignalExecutionResult(boundaryResult.Record, boundaryResult);
                }

                return new AISignalExecutionResult(null, boundaryResult);
            }
            finally
            {
                strategy.Side = originalSide;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "硬性边界处理失败: {StrategyId}", strategy.Id);
            return AISignalExecutionResult.Failed;
        }
    }

    /// <summary>
    /// 进场下单：市价直下，或 maker 优先（post-only 挂单；拒单立即回退市价）。
    ///
    /// maker 优先的价值：maker 与 taker 的费率差（常见 0.02% vs 0.05%）是薄利策略里
    /// 唯一可确定的正期望，长期胜率不变也能显著改善盈亏比。代价是可能错过快速入场点。
    /// 回退规则（资金安全优先）：
    /// - 挂单成功（含部分成交后余量挂盘等待）→ 返回挂单结果，不追单（避免双重成交）；
    /// - post-only 被交易所拒（GTX/LIMIT_MAKER 会立即吃单时拒单，成交量为 0）→ 立即回退市价；
    /// - 网络类失败 → 等待一个超时窗口后回退市价（给短暂抖动留恢复窗口）；
    /// - 风控拒绝 / 用户拒绝 / 告警联动拒绝 → 原样返回，绝不回退市价——
    ///   用市价绕过"用户刚拒绝过的同一笔交易"等于架空风控与人工确认。
    /// 未成交的余量由 PendingOrderTimeoutService 超时撤单告警，不自动追单。
    /// </summary>
    private async Task<TradeResult> ExecuteEntryOrderAsync(
        TradingStrategy strategy, AISignalParams aiParams, OrderSide entrySide,
        decimal quantity, decimal currentPrice, AISignalDecision decision,
        bool requireClose, CancellationToken ct)
    {
        // 离场单（现货 SELL 平仓）不做 maker 优先：平仓要的是确定性离场，不是省手续费
        if (!aiParams.IsMakerFirst || requireClose)
        {
            return await _tradeExecutor.ExecuteOrderAsync(
                strategy.Symbol, entrySide, OrderType.Market, quantity, currentPrice,
                strategyId: strategy.Id, aiReasoning: decision.Reason,
                requireClose: requireClose, ct: ct).ConfigureAwait(false);
        }

        var timeoutSeconds = aiParams.MakerTimeoutSeconds > 0 ? aiParams.MakerTimeoutSeconds : 10;

        // post-only 挂在盘口的己方一侧（买挂买一、卖挂卖一），只做 maker；
        // 价格用当前价，穿越盘口时交易所会拒单（合约 GTX / 现货 LIMIT_MAKER），此时立即回退市价
        var makerResult = await _tradeExecutor.ExecuteOrderAsync(
            strategy.Symbol, entrySide, OrderType.PostOnlyLimit, quantity, currentPrice,
            limitPrice: currentPrice, strategyId: strategy.Id,
            aiReasoning: decision.Reason, requireClose: false, ct: ct).ConfigureAwait(false);

        // 挂上或部分成交：返回挂单结果。余量由超时撤单管理，护栏按本单预期数量先行持久化
        //（交易所侧护栏腿由对账按持仓门控挂出，挂单未成交前不会产生裸卖单）
        if (makerResult.Success && makerResult.Record is { Status: not TradeRecordStatus.Failed })
            return makerResult;

        // post-only 拒单（成交量 0、状态 REJECTED/EXPIRED 映射为 Failed）：盘口已穿越，
        // 行情在快速推进，立即回退市价
        if (makerResult.Success)
        {
            _logger.LogInformation(
                "post-only 挂单被交易所拒绝（行情已穿越盘口），立即回退市价: {StrategyId} {Symbol} {Side}",
                strategy.Id, strategy.Symbol, entrySide);
        }
        else if (makerResult.FailureCategory == TradeFailureCategory.Network)
        {
            _logger.LogInformation(
                "maker 优先下单网络失败（{Reason}），{Timeout}s 后回退市价: {StrategyId} {Symbol} {Side}",
                makerResult.ErrorMessage, timeoutSeconds, strategy.Id, strategy.Symbol, entrySide);

            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct).ConfigureAwait(false);
        }
        else if (makerResult.FailureCategory is TradeFailureCategory.Rejected or TradeFailureCategory.OrderStateUnknown)
        {
            // 风控/人工拒绝不得被市价单绕过；订单状态未知时原 maker 单可能已落地，
            // 两种情况下都必须把结果交给上层暂停策略，不能再下第二张单。
            return makerResult;
        }
        else
        {
            // 其余业务类失败（余额不足、精度问题等）：市价重试一次大概率同样失败且无额外风险
            _logger.LogInformation(
                "maker 优先下单业务失败（{Reason}），立即回退市价: {StrategyId} {Symbol} {Side}",
                makerResult.ErrorMessage, strategy.Id, strategy.Symbol, entrySide);
        }

        return await _tradeExecutor.ExecuteOrderAsync(
            strategy.Symbol, entrySide, OrderType.Market, quantity, currentPrice,
            strategyId: strategy.Id, aiReasoning: decision.Reason,
            requireClose: false, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 影子模式的硬性边界退出：记账不平仓。持仓数量取自影子账本中未平完的开仓行，
    /// 没有未平仓量时直接完结策略（与实盘路径的"持仓已消失"语义一致）。
    /// </summary>
    private async Task<AISignalExecutionResult> ExecuteWithAgentAsync(TradingStrategy strategy,
        decimal currentPrice,
        AISignalParams aiParams,
        CancellationToken ct)
    {
        try
        {
            // maker 优先的进场单可能长时间挂在盘口未成交：挂单在途期间不做新的开仓决策，
            // 否则下个评估周期会在"账面仍无持仓"的情况下再挂一张同类买单，双重成交
            var unsettledRecords = await _dataService.GetUnsettledTradeRecordsAsync(strategy.Symbol, ct)
                .ConfigureAwait(false);
            if (unsettledRecords.Any(r =>
                    string.Equals(r.StrategyId, strategy.Id, StringComparison.OrdinalIgnoreCase)
                    && r.OrderType is OrderType.Limit or OrderType.PostOnlyLimit))
            {
                _logger.LogInformation(
                    "AI 进场挂单仍在途，跳过本轮决策避免双重成交: {StrategyId} {Symbol}",
                    strategy.Id, strategy.Symbol);
                return AISignalExecutionResult.NoTrade;
            }

            var priorRecords = await _dataService.GetRecordsByStrategyAsync(strategy.Id, ct)
                .ConfigureAwait(false);

            var prompt = await BuildAIPromptAsync(strategy, currentPrice, priorRecords, aiParams, ct)
                .ConfigureAwait(false);
            var responseText = await InvokeAgentAsync(prompt, ct).ConfigureAwait(false);

            if (!AISignalDecisionParser.TryParse(responseText, out var decision) || decision.IsHold)
            {
                _logger.LogInformation(
                    "AI 决策为 HOLD 或无法解析: {StrategyId} 响应片段 {ResponseSnippet}",
                    strategy.Id, Truncate(responseText, 200));
                return AISignalExecutionResult.NoTrade;
            }

            return await ExecuteDecisionAsync(strategy, currentPrice, decision, aiParams, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 信号策略执行失败: {StrategyId}", strategy.Id);
            return AISignalExecutionResult.Failed;
        }
    }

    /// <summary>
    /// 执行 AI 结构化决策：置信度门控 → 置信度动态仓位（按档案预算与仓位上限封顶）→
    /// 本地下单（AI 不直接调用下单工具）→ 成交后自动附加止盈止损护栏。
    /// </summary>
    private async Task<AISignalExecutionResult> ExecuteDecisionAsync(
        TradingStrategy strategy,
        decimal currentPrice,
        AISignalDecision decision,
        AISignalParams aiParams,
        CancellationToken ct)
    {
        if (decision.Confidence < aiParams.ConfidenceThreshold)
        {
            _logger.LogInformation(
                "AI 置信度 {Confidence} 低于门槛 {Threshold}，放弃执行: {StrategyId} 理由: {Reason}",
                decision.Confidence, aiParams.ConfidenceThreshold, strategy.Id, decision.Reason);
            return AISignalExecutionResult.NoTrade;
        }

        // 方向由 AI 决策驱动：BUY 开多 / SELL 开空（合约）或离场（现货）。
        // 早期实现固定使用 strategy.Side，导致 AI 的 SELL 决策只进日志、不改变任何行为——
        // 策略被永久锁在单一方向，"AI 决策"名不副实。
        var entrySide = decision.IsBuy ? OrderSide.Buy : OrderSide.Sell;
        var isFutures = _tradeExecutor.IsFuturesMode;
        // 现货无法开空：SELL 只能表达"离场"，交给 requireClose 兜底（无持仓时被拒，不会反向做空）
        var requireClose = !isFutures && entrySide == OrderSide.Sell;

        var budget = aiParams.BudgetUsdt > 0 ? aiParams.BudgetUsdt : strategy.Quantity;
        if (budget <= 0)
        {
            _logger.LogWarning(
                "AI 策略未配置开仓预算（BudgetUsdt/Quantity），跳过执行: {StrategyId}", strategy.Id);
            return AISignalExecutionResult.NoTrade;
        }

        // 置信度动态仓位：预算 × 置信度系数，并按档案仓位上限封顶（AI 无法突破）
        var sizedBudget = budget * (decision.Confidence / 100m);
        var balanceSummary = await _portfolioService.GetAccountBalanceSummaryAsync(ct, useCache: false).ConfigureAwait(false);
        var accountValue = balanceSummary?.TotalValueUSDT ?? 0;
        if (accountValue > 0)
        {
            var capValue = accountValue * aiParams.MaxPositionPercent / 100m;
            if (sizedBudget > capValue)
            {
                _logger.LogInformation(
                    "预算 {Budget:F2} 超出仓位上限 {Cap:F2}（账户总值 {AccountValue:F2} × {MaxPercent}%），已封顶: {StrategyId}",
                    sizedBudget, capValue, accountValue, aiParams.MaxPositionPercent, strategy.Id);
                sizedBudget = capValue;
            }
        }

        var quantity = currentPrice > 0 ? Math.Round(sizedBudget / currentPrice, 8) : 0;
        if (quantity <= 0)
        {
            _logger.LogInformation("计算后下单数量为 0，跳过执行: {StrategyId} 预算 {Budget}", strategy.Id, sizedBudget);
            return AISignalExecutionResult.NoTrade;
        }

        if (aiParams.ShadowMode)
        {
            // 影子模式：记账不下单。按当前价模拟成交，使策略绩效可被验证后再上实盘
            var pnl = await _shadowTradingService.RecordAsync(
                strategy.Id, strategy.Symbol, entrySide, quantity, currentPrice, ct: ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "影子模式决策（仅记账不下单）: {StrategyId} {Symbol} {Action} 置信度 {Confidence}% 数量 {Qty} " +
                "止损 {StopLoss} 止盈 {TakeProfit} 盈亏 {Pnl} 理由: {Reason}",
                strategy.Id, strategy.Symbol, decision.Action, decision.Confidence, quantity,
                decision.StopLossPrice, decision.TakeProfitPrice, pnl, decision.Reason);
            return AISignalExecutionResult.NoTrade;
        }

        var result = await ExecuteEntryOrderAsync(
            strategy, aiParams, entrySide, quantity, currentPrice, decision, requireClose, ct)
            .ConfigureAwait(false);

        if (!result.Success || result.Record == null)
            return new AISignalExecutionResult(null, result);

        // 成交后自动附加护栏：本地按决策价/档案兜底价生成并持久化，下个 tick 即生效
        var effectiveExecutedQty = result.Record.ExecutedQty > 0 ? result.Record.ExecutedQty : quantity;
        await ApplyGuardrailsAsync(strategy, aiParams, entrySide, currentPrice, effectiveExecutedQty, decision, ct)
            .ConfigureAwait(false);
        await _dataService.UpdateStrategyTriggeredAsync(strategy.Id, ct).ConfigureAwait(false);

        return new AISignalExecutionResult(result.Record, result);
    }

    /// <summary>
    /// 构建交易决策 Prompt：策略参数、当前仓位、近期成交摘要与最新市场分析报告。
    /// </summary>
    private async Task<string> BuildAIPromptAsync(
        TradingStrategy strategy,
        decimal currentPrice,
        List<TradeRecord> priorRecords,
        AISignalParams aiParams,
        CancellationToken ct)
    {
        var recentSummary = priorRecords.
            Take(5)
            .Aggregate(new StringBuilder(), (sb, r) => sb.AppendLine($"{r.CreatedAt:u} {r.Side} 成交量:{r.ExecutedQty} 价:{r.ExecutedPrice} {r.Status}"))
            .ToString().TrimEnd();

        var positionSummary = await BuildPositionSummaryAsync(strategy.Symbol, ct).ConfigureAwait(false);
        var analysisContext = BuildAnalysisContext(strategy.Symbol);

        var budget = aiParams.BudgetUsdt > 0 ? aiParams.BudgetUsdt : strategy.Quantity;
        var todayStats = await _dataService.GetTodayStatsAsync(ct).ConfigureAwait(false);
        var maxDailyTrades = (await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false)).MaxDailyTrades;
        var remainingTrades = Math.Max(0, maxDailyTrades - todayStats.TradeCount);

        const string jsonTemplate = """
            {
              "decision": "BUY | SELL | HOLD",
              "confidence": 0,
              "stopLossPrice": null,
              "takeProfitPrice": null,
              "reason": "一句话决策理由"
            }
            """;

        return $"""
            你是虚拟币智能交易决策引擎，负责 {strategy.Symbol} 的交易决策。当前价格 {currentPrice}。

            ## 输出格式（强制）
            你的最终回答必须且只能是一个 JSON 对象，格式如下，禁止输出任何其他文字、markdown 代码块标记或工具调用说明。
            下单由系统完成，你没有下单能力；你可以调用行情、持仓、技术指标等查询工具辅助决策。
            {jsonTemplate}

            ## 决策约束
            - 置信度低于 {aiParams.ConfidenceThreshold} 时必须 HOLD（系统会强制拦截，低于门槛的 BUY/SELL 不会执行）。
            - decision 为 BUY 时：stopLossPrice 必须低于当前价、takeProfitPrice 必须高于当前价；SELL 相反。
            - 无法给出合理止盈/止损价时填 null，系统会按风险档案「{aiParams.ParsedRiskProfile.GetDisplayName()}」自动生成护栏。
            - 本次开仓预算约 {budget:F2} USDT，实际下单数量由系统按置信度与仓位上限计算，你无需输出数量。
            - 仓位上限为账户总值的 {aiParams.MaxPositionPercent:F1}%（系统强制执行）。
            - 今日已实现盈亏 {todayStats.TotalPnl:F2} USDT，今日剩余交易次数 {remainingTrades}。

            ## 当前仓位状态
            {positionSummary}

            ## 最新市场分析报告
            {analysisContext}

            近期该策略成交摘要（最多 5 笔，按时间倒序）:
            {recentSummary}
            """;
    }

    /// <summary>
    /// 调用交易 Agent 并返回原始响应文本（由本地解析为结构化决策）。
    /// </summary>
    private async Task<string> InvokeAgentAsync(string prompt, CancellationToken ct)
    {
        var agent = _agentFactory.CreateAutomationAgent();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var response = await agent.RunAsync(messages, session: null, options: null, cancellationToken: ct)
            .ConfigureAwait(false);
        _logger.LogDebug("TradingAgent 响应: {Content}", response.Text);
        return response.Text;
    }

    private async Task<string> BuildPositionSummaryAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var positions = await _portfolioService.GetCurrentPositionsAsync(ct).ConfigureAwait(false);
            var symbolPosition = positions.FirstOrDefault(p =>
                p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            var positionLine = symbolPosition != null
                ? $"持仓: 数量 {symbolPosition.Quantity} | 入场均价 {symbolPosition.EntryPrice} | 未实现盈亏 {symbolPosition.UnrealizedPnl:F2} USDT ({symbolPosition.UnrealizedPnlPercent:F1}%)"
                : "当前无持仓";

            var activeStrategies = await _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active, ct)
                .ConfigureAwait(false);
            var siblings = activeStrategies
                .Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                .Select(s => $"{s.Type}(触发价:{s.TriggerPrice})")
                .ToList();
            var siblingsLine = siblings.Count > 0 ? string.Join(", ", siblings) : "无";

            return $"{positionLine}\n同标的活跃策略: {siblingsLine}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取持仓信息失败，略过仓位上下文: {Symbol}", symbol);
            return "（获取持仓信息失败）";
        }
    }

    private string BuildAnalysisContext(string symbol)
    {
        // 后台 AI 交易固定运行在虚拟币市场，显式传入市场类型，
        // 不依赖随时可能被 UI 切换的全局 MarketContext
        var cached = _reportCache.Get(symbol, MarketType.Crypto);
        if (cached == null)
            return "（暂无分析报告，建议先运行市场分析工作流）";

        var ageMinutes = (int)(DateTime.UtcNow - cached.CachedAt).TotalMinutes;
        var result = cached.Report.CoordinatorResult;

        var sb = new StringBuilder();
        sb.AppendLine($"报告时间: {ageMinutes} 分钟前");
        sb.AppendLine($"综合评级: {result.InvestmentRating} | 综合评分: {result.OverallScore:F1}/10 | 置信度: {result.ConfidencePercentage:F0}%");
        sb.AppendLine($"目标价区间: {result.TargetPrice} | 预期: {result.PriceChangeExpectation}");
        sb.AppendLine($"技术面: {result.DimensionScores.Technical:F1} | 情绪面: {result.DimensionScores.Sentiment:F1} | 风险等级: {result.RiskLevel}");
        sb.AppendLine($"结论: {result.Summary}");

        if (result.OperationSuggestions.Count > 0)
        {
            sb.AppendLine("操作建议:");
            foreach (var suggestion in result.OperationSuggestions.Take(3))
                sb.AppendLine($"  - {suggestion}");
        }

        if (result.RiskFactors.Count > 0)
            sb.AppendLine($"主要风险: {string.Join("; ", result.RiskFactors.Take(2))}");

        return sb.ToString().TrimEnd();
    }
}

public sealed class AISignalExecutionResult
{
    /// <summary>
    /// AI 决策为 HOLD 或策略已自然完结（无持仓），属正常路径：不进失败冷却，
    /// 重试频率由 StrategyEngine 的 LastTriggeredAt（analysisInterval）节流。
    /// </summary>
    public static AISignalExecutionResult NoTrade { get; } = new(null, null, AISignalOutcome.NoTrade);

    /// <summary>
    /// 执行过程发生异常。进入失败冷却，防止每个价格 tick 重复失败。
    /// </summary>
    public static AISignalExecutionResult Failed { get; } = new(null, null, AISignalOutcome.Failed);

    /// <summary>AI 策略参数缺失或损坏。策略已暂停，不进入普通失败冷却。</summary>
    public static AISignalExecutionResult ConfigurationFailed(string reason) =>
        new(null, new TradeResult
        {
            Success = false,
            ErrorMessage = reason,
            FailureCategory = TradeFailureCategory.Rejected
        }, AISignalOutcome.ConfigurationFailed);

    public AISignalExecutionResult(TradeRecord? record)
        : this(record, null, record != null ? AISignalOutcome.Executed : AISignalOutcome.NoTrade)
    {
    }

    public AISignalExecutionResult(TradeRecord? record, TradeResult? tradeResult)
        : this(record, tradeResult, record != null ? AISignalOutcome.Executed : AISignalOutcome.Failed)
    {
    }

    private AISignalExecutionResult(TradeRecord? record, TradeResult? tradeResult, AISignalOutcome outcome)
    {
        Record = record;
        TradeResult = tradeResult;
        Outcome = outcome;
    }

    public TradeRecord? Record { get; }
    public TradeResult? TradeResult { get; }
    public AISignalOutcome Outcome { get; }
    public bool TradeExecuted => Record != null;
}

/// <summary>
/// AISignal 策略单次执行的结局
/// </summary>
public enum AISignalOutcome
{
    /// <summary>已成交（含硬性边界平仓成功）</summary>
    Executed,

    /// <summary>未交易且无异常（HOLD 决策 / 无持仓完结）</summary>
    NoTrade,

    /// <summary>执行异常（网络、Agent 调用失败等）</summary>
    Failed,

    /// <summary>AI 策略参数缺失或损坏，策略已暂停等待人工修复</summary>
    ConfigurationFailed
}
