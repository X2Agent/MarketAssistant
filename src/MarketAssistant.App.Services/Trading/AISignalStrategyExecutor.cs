using System.Text;
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
            var priorLatestRecordId = priorRecords.FirstOrDefault()?.Id;

            var prompt = await BuildAIPromptAsync(strategy, currentPrice, priorRecords, ct)
                .ConfigureAwait(false);
            await InvokeAgentAsync(prompt, ct).ConfigureAwait(false);

            return await ProcessAgentResponseAsync(strategy, priorLatestRecordId, ct)
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
        var recentSummary = priorRecords.Count == 0
            ? "（该策略尚无成交记录）"
            : string.Join("\n", priorRecords.Take(5).Select(r =>
                $"{r.CreatedAt:u} {r.Side} 成交量:{r.ExecutedQty} 价:{r.ExecutedPrice} {r.Status}"));

        var positionSummary = await BuildPositionSummaryAsync(strategy.Symbol, ct).ConfigureAwait(false);
        var analysisContext = BuildAnalysisContext(strategy.Symbol);

        var stopLossInfo = strategy.StopLossPrice.HasValue
            ? $"止损价: {strategy.StopLossPrice.Value}"
            : "未设置止损";
        var takeProfitInfo = strategy.TakeProfitPrice.HasValue
            ? $"止盈价: {strategy.TakeProfitPrice.Value}"
            : "未设置止盈";
        var maxPositionPercent = strategy.MaxPositionPercent ?? 20m;
        var todayStats = await _dataService.GetTodayStatsAsync(ct).ConfigureAwait(false);
        var maxDailyTrades = (await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false)).MaxDailyTrades;
        var remainingTrades = Math.Max(0, maxDailyTrades - todayStats.TradeCount);

        return $"""
            分析交易标的 {strategy.Symbol}，当前价格 {currentPrice}。

            ## 风险预算（必须严格遵守）
            - 本次交易后该 symbol 总仓位不得超过账户总值的 {maxPositionPercent:F1}%
            - 今日已实现盈亏: {todayStats.TotalPnl:F2} USDT
            - 今日剩余交易次数: {remainingTrades}

            ## 策略配置
            {strategy.CustomParams ?? "无"}
            风险边界: {stopLossInfo} | {takeProfitInfo}

            ## 当前仓位状态
            {positionSummary}

            ## 最新市场分析报告
            {analysisContext}

            近期该策略成交摘要（最多 5 笔，按时间倒序）:
            {recentSummary}

            ## 决策要求
            请输出结构化决策：
            1. 决策: BUY / SELL / HOLD
            2. 置信度: 0-100
            3. 入场逻辑
            4. 退出计划（止损/止盈具体价位）
            5. 主要风险因素

            如果置信度低于 60，建议 HOLD。
            如果决定交易，请调用 PlaceOrder 工具执行 {strategy.Side} 操作，数量 {strategy.Quantity}。
            如果决定不交易，请说明理由。
            """;
    }

    private async Task InvokeAgentAsync(string prompt, CancellationToken ct)
    {
        var agent = _agentFactory.CreateAutomationAgent();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var response = await agent.RunAsync(messages, session: null, options: null, cancellationToken: ct)
            .ConfigureAwait(false);
        _logger.LogDebug("TradingAgent 响应: {Content}", response.Text);
    }

    private async Task<AISignalExecutionResult> ProcessAgentResponseAsync(
        TradingStrategy strategy,
        string? priorLatestRecordId,
        CancellationToken ct)
    {
        var recentRecords = await _dataService.GetRecordsByStrategyAsync(strategy.Id, ct)
            .ConfigureAwait(false);
        var newestRecord = recentRecords.FirstOrDefault();
        if (newestRecord == null || newestRecord.Id == priorLatestRecordId)
            return AISignalExecutionResult.NoTrade;

        await _dataService.UpdateStrategyTriggeredAsync(strategy.Id, ct).ConfigureAwait(false);
        return new AISignalExecutionResult(newestRecord);
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