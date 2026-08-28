using System.Text;
using System.Text.Json;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Trading.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// AI 信号策略执行器：封装硬性边界处理、Prompt 构建、Agent 调用与成交回写识别。
/// </summary>
public sealed class AISignalStrategyExecutor
{
    private readonly ITradingAgentFactory _agentFactory;
    private readonly TradingDataService _dataService;
    private readonly TradingStrategyService _strategyService;
    private readonly CryptoPortfolioService _portfolioService;
    private readonly AnalysisReportCache _reportCache;
    private readonly TradeExecutor _tradeExecutor;
    private readonly ILogger<AISignalStrategyExecutor> _logger;

    public AISignalStrategyExecutor(
        ITradingAgentFactory agentFactory,
        TradingDataService dataService,
        TradingStrategyService strategyService,
        CryptoPortfolioService portfolioService,
        AnalysisReportCache reportCache,
        TradeExecutor tradeExecutor,
        ILogger<AISignalStrategyExecutor> logger)
    {
        _agentFactory = agentFactory;
        _dataService = dataService;
        _strategyService = strategyService;
        _portfolioService = portfolioService;
        _reportCache = reportCache;
        _tradeExecutor = tradeExecutor;
        _logger = logger;
    }

    public async Task<AISignalExecutionResult> ExecuteAsync(
        TradingStrategy strategy,
        decimal currentPrice,
        CancellationToken ct = default)
    {
        if (TryHandleHardBoundary(strategy, currentPrice, out var boundaryReasoning))
        {
            return await ExecuteHardBoundaryAsync(strategy, currentPrice, boundaryReasoning, ct)
                .ConfigureAwait(false);
        }

        return await ExecuteWithAgentAsync(strategy, currentPrice, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 硬性边界（止损/止盈）处理：确认仍有持仓后反向平仓。
    /// 平仓成功或持仓已消失时完结策略，防止退出条件兑现后策略按评估间隔反复触发
    /// （TradeExecutor 仅在成功时回写触发计数，失败时保持 Active 留待下个冷却期重试）。
    /// </summary>
    private async Task<AISignalExecutionResult> ExecuteHardBoundaryAsync(
        TradingStrategy strategy,
        decimal currentPrice,
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

            // TradeExecutor 从 strategy 读取执行方向；用 try/finally 把翻转限制在本次调用内，
            // 避免"持仓方向"语义被平仓动作污染（当前策略对象每次评估重新加载，此处防御性恢复使约定显式化）
            var originalSide = strategy.Side;
            try
            {
                strategy.Side = originalSide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

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

    private async Task<AISignalExecutionResult> ExecuteWithAgentAsync(
        TradingStrategy strategy,
        decimal currentPrice,
        CancellationToken ct)
    {
        try
        {
            TradingContext.CurrentStrategyId = strategy.Id;

            var priorRecords = await _dataService.GetRecordsByStrategyAsync(strategy.Id, ct)
                .ConfigureAwait(false);

            var prompt = await BuildAIPromptAsync(strategy, currentPrice, priorRecords, ct)
                .ConfigureAwait(false);
            var responseText = await InvokeAgentAsync(prompt, ct).ConfigureAwait(false);

            if (!AISignalDecisionParser.TryParse(responseText, out var decision) || decision!.IsHold)
            {
                _logger.LogInformation(
                    "AI 决策为 HOLD 或无法解析: {StrategyId} 响应片段 {ResponseSnippet}",
                    strategy.Id, Truncate(responseText, 200));
                return AISignalExecutionResult.NoTrade;
            }

            return await ExecuteDecisionAsync(strategy, currentPrice, decision!, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 信号策略执行失败: {StrategyId}", strategy.Id);
            return AISignalExecutionResult.Failed;
        }
        finally
        {
            TradingContext.CurrentStrategyId = null;
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
        CancellationToken ct)
    {
        var aiParams = AISignalParams.FromJson(strategy.CustomParams) ?? new AISignalParams();

        if (decision.Confidence < aiParams.ConfidenceThreshold)
        {
            _logger.LogInformation(
                "AI 置信度 {Confidence} 低于门槛 {Threshold}，放弃执行: {StrategyId} 理由: {Reason}",
                decision.Confidence, aiParams.ConfidenceThreshold, strategy.Id, decision.Reason);
            return AISignalExecutionResult.NoTrade;
        }

        var entrySide = strategy.Side;
        var budget = aiParams.BudgetUsdt > 0 ? aiParams.BudgetUsdt : strategy.Quantity;
        if (budget <= 0)
        {
            _logger.LogWarning(
                "AI 策略未配置开仓预算（BudgetUsdt/Quantity），跳过执行: {StrategyId}", strategy.Id);
            return AISignalExecutionResult.NoTrade;
        }

        // 置信度动态仓位：预算 × 置信度系数，并按档案仓位上限封顶（AI 无法突破）
        var sizedBudget = budget * (decision.Confidence / 100m);
        var balanceSummary = await _portfolioService.GetAccountBalanceSummaryAsync(ct).ConfigureAwait(false);
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
            _logger.LogInformation(
                "影子模式决策（仅记录不下单）: {StrategyId} {Symbol} {Action} 置信度 {Confidence}% 数量 {Qty} " +
                "止损 {StopLoss} 止盈 {TakeProfit} 理由: {Reason}",
                strategy.Id, strategy.Symbol, decision.Action, decision.Confidence, quantity,
                decision.StopLossPrice, decision.TakeProfitPrice, decision.Reason);
            return AISignalExecutionResult.NoTrade;
        }

        var result = await _tradeExecutor.ExecuteOrderAsync(
            strategy.Symbol,
            entrySide,
            OrderType.Market,
            quantity,
            currentPrice,
            strategyId: strategy.Id,
            aiReasoning: decision.Reason,
            requireClose: false,
            ct: ct).ConfigureAwait(false);

        if (!result.Success || result.Record == null)
            return new AISignalExecutionResult(null, result);

        // 成交后自动附加护栏：本地按决策价/档案兜底价生成并持久化，下个 tick 即生效
        await ApplyGuardrailsAsync(strategy, aiParams, entrySide, currentPrice, quantity, decision, ct)
            .ConfigureAwait(false);
        await _dataService.UpdateStrategyTriggeredAsync(strategy.Id, ct).ConfigureAwait(false);

        return new AISignalExecutionResult(result.Record, result);
    }

    private static string? Truncate(string? text, int maxLength)
        => string.IsNullOrEmpty(text) || text.Length <= maxLength ? text : text[..maxLength] + "…";

    /// <summary>
    /// 生成并持久化护栏：
    /// - TrailingStop 出场：创建独立的追踪止损伴随策略（复用引擎现有追踪评估，一次性执行）；
    /// - FixedStop 出场：止损/止盈价写入策略的 StopLossPrice/TakeProfitPrice（硬性边界机制）。
    /// AI 给出的价位仅在与当前价方向关系合理时采用，否则按档案百分比兜底，保证护栏方向永不颠倒。
    /// </summary>
    private async Task ApplyGuardrailsAsync(
        TradingStrategy strategy,
        AISignalParams aiParams,
        OrderSide entrySide,
        decimal currentPrice,
        decimal executedQty,
        AISignalDecision decision,
        CancellationToken ct)
    {
        var isLong = entrySide == OrderSide.Buy;

        if (aiParams.ParsedExitStyle == ExitStyle.TrailingStop)
        {
            var trailingPercent = aiParams.TrailingPercent > 0
                ? aiParams.TrailingPercent
                : ScenarioPresets.GetTrailingPercent(aiParams.ParsedRiskProfile);
            var companion = new TradingStrategy
            {
                Symbol = strategy.Symbol,
                Type = StrategyType.TrailingStop,
                Status = StrategyStatus.Active,
                // 多头入场 → 追踪卖出出场；空头入场 → 追踪买入出场
                Side = isLong ? OrderSide.Sell : OrderSide.Buy,
                TriggerPrice = currentPrice,
                Quantity = executedQty,
                MaxExecutions = 1,
                CustomParams = JsonSerializer.Serialize(new
                {
                    trailingPercent,
                    activationPrice = currentPrice
                })
            };
            await _strategyService.SaveStrategyAsync(companion, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "AI 已创建追踪止损伴随策略: {CompanionId} 回调 {Percent}% 激活价 {Activation} 关联 {StrategyId}",
                companion.Id, trailingPercent, currentPrice, strategy.Id);
            return;
        }

        decimal? aiStopLoss = decision.StopLossPrice;
        var stopLossValid = aiStopLoss.HasValue &&
            (isLong ? aiStopLoss.Value < currentPrice : aiStopLoss.Value > currentPrice);
        var stopLossFallback = isLong
            ? currentPrice * (1 - aiParams.StopLossPercent / 100m)
            : currentPrice * (1 + aiParams.StopLossPercent / 100m);
        var stopLoss = Math.Round(stopLossValid ? aiStopLoss!.Value : stopLossFallback, 8);

        decimal? aiTakeProfit = decision.TakeProfitPrice;
        var takeProfitValid = aiTakeProfit.HasValue &&
            (isLong ? aiTakeProfit.Value > currentPrice : aiTakeProfit.Value < currentPrice);
        var takeProfitFallback = isLong
            ? currentPrice * (1 + aiParams.TakeProfitPercent / 100m)
            : currentPrice * (1 - aiParams.TakeProfitPercent / 100m);
        var takeProfit = Math.Round(takeProfitValid ? aiTakeProfit!.Value : takeProfitFallback, 8);

        strategy.StopLossPrice = stopLoss;
        strategy.TakeProfitPrice = takeProfit;
        await _dataService.UpdateStrategyGuardrailsAsync(strategy.Id, stopLoss, takeProfit, ct)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "AI 护栏已附加: {StrategyId} 止损 {StopLoss} 止盈 {TakeProfit}（{Source}）",
            strategy.Id, stopLoss, takeProfit,
            decision.StopLossPrice.HasValue || decision.TakeProfitPrice.HasValue
                ? "AI 决策价" : "风险档案兜底价");
    }

    private static bool TryHandleHardBoundary(
        TradingStrategy strategy,
        decimal currentPrice,
        out string reasoning)
    {
        reasoning = string.Empty;

        if (strategy.ExecutionCount == 0)
            return false;

        if (strategy.StopLossPrice.HasValue)
        {
            var stopTriggered = strategy.Side == OrderSide.Buy
                ? currentPrice <= strategy.StopLossPrice.Value
                : currentPrice >= strategy.StopLossPrice.Value;
            if (stopTriggered)
            {
                reasoning = $"AISignal 硬性止损触发：当前价 {currentPrice} 已达止损位 {strategy.StopLossPrice.Value}，系统强制平仓";
                return true;
            }
        }

        if (strategy.TakeProfitPrice.HasValue)
        {
            var tpTriggered = strategy.Side == OrderSide.Buy
                ? currentPrice >= strategy.TakeProfitPrice.Value
                : currentPrice <= strategy.TakeProfitPrice.Value;
            if (tpTriggered)
            {
                reasoning = $"AISignal 硬性止盈触发：当前价 {currentPrice} 已达止盈位 {strategy.TakeProfitPrice.Value}，系统自动止盈";
                return true;
            }
        }

        return false;
    }

    private async Task<string> BuildAIPromptAsync(
        TradingStrategy strategy,
        decimal currentPrice,
        List<TradeRecord> priorRecords,
        CancellationToken ct)
    {
        var recentSummary = priorRecords.
            Take(5)
            .Aggregate(new StringBuilder(), (sb, r) => sb.AppendLine($"{r.CreatedAt:u} {r.Side} 成交量:{r.ExecutedQty} 价:{r.ExecutedPrice} {r.Status}"))
            .ToString().TrimEnd();

        var positionSummary = await BuildPositionSummaryAsync(strategy.Symbol, ct).ConfigureAwait(false);
        var analysisContext = BuildAnalysisContext(strategy.Symbol);

        var aiParams = AISignalParams.FromJson(strategy.CustomParams) ?? new AISignalParams();
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
            下单由系统完成，禁止调用任何下单（PlaceOrder）工具；你可以调用行情、持仓等技术指标查询工具辅助决策。
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
        var cached = _reportCache.Get(symbol);
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
    Failed
}