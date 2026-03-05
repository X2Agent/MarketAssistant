using MarketAssistant.Agents.Trading;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Data;
using MarketAssistant.Trading.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 后台市场监控器，订阅实时价格并根据策略触发交易
/// </summary>
public class MarketMonitor : IDisposable
{
    private readonly BinanceWebSocketService _webSocketService;
    private readonly StrategyEngine _strategyEngine;
    private readonly TradeExecutor _tradeExecutor;
    private readonly ITradingAgentFactory _agentFactory;
    private readonly TradingDataService _dataService;
    private readonly ILogger<MarketMonitor> _logger;

    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);

    public bool IsRunning => _isRunning;

    /// <summary>
    /// 当交易执行时触发的事件（UI 可订阅来刷新）
    /// </summary>
    public event Action<TradeRecord>? TradeExecuted;

    /// <summary>
    /// 当监控状态变化时触发
    /// </summary>
    public event Action<bool>? StatusChanged;

    public MarketMonitor(
        BinanceWebSocketService webSocketService,
        StrategyEngine strategyEngine,
        TradeExecutor tradeExecutor,
        ITradingAgentFactory agentFactory,
        TradingDataService dataService,
        ILogger<MarketMonitor> logger)
    {
        _webSocketService = webSocketService;
        _strategyEngine = strategyEngine;
        _tradeExecutor = tradeExecutor;
        _agentFactory = agentFactory;
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>
    /// 启动后台监控
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            return;

        _cts = new CancellationTokenSource();
        _isRunning = true;

        var activeStrategies = await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active);
        var symbols = activeStrategies
            .Select(s => s.Symbol.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (symbols.Count > 0)
        {
            await _webSocketService.SubscribeAsync(symbols);
        }

        _webSocketService.PriceUpdated += OnPriceUpdated;

        _logger.LogInformation("MarketMonitor 已启动，监控 {Count} 个交易对", symbols.Count);
        StatusChanged?.Invoke(true);
    }

    /// <summary>
    /// 停止后台监控
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _webSocketService.PriceUpdated -= OnPriceUpdated;
        _cts?.Cancel();

        await _webSocketService.UnsubscribeAllAsync();

        _isRunning = false;
        _logger.LogInformation("MarketMonitor 已停止");
        StatusChanged?.Invoke(false);
    }

    /// <summary>
    /// 刷新监控的交易对列表（策略增减后调用）
    /// </summary>
    public async Task RefreshSubscriptionsAsync()
    {
        if (!_isRunning)
            return;

        await _webSocketService.UnsubscribeAllAsync();

        var activeStrategies = await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active);
        var symbols = activeStrategies
            .Select(s => s.Symbol.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (symbols.Count > 0)
        {
            await _webSocketService.SubscribeAsync(symbols);
        }

        _logger.LogInformation("已刷新监控列表: {Count} 个交易对", symbols.Count);
    }

    private void OnPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        if (_cts?.IsCancellationRequested == true)
            return;

        _ = ProcessPriceUpdateAsync(symbol, lastPrice);
    }

    private async Task ProcessPriceUpdateAsync(string symbol, decimal lastPrice)
    {
        if (!await _evaluationLock.WaitAsync(0))
            return;

        try
        {
            var triggered = await _strategyEngine.EvaluateStrategiesAsync(
                symbol, lastPrice, _cts?.Token ?? CancellationToken.None);

            foreach (var strategy in triggered)
            {
                await HandleTriggeredStrategyAsync(strategy, lastPrice);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，忽略
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "价格更新处理异常: {Symbol}", symbol);
        }
        finally
        {
            _evaluationLock.Release();
        }
    }

    private async Task HandleTriggeredStrategyAsync(TradingStrategy strategy, decimal currentPrice)
    {
        if (strategy.Type == StrategyType.AISignal)
        {
            await HandleAISignalAsync(strategy, currentPrice);
        }
        else
        {
            var result = await _tradeExecutor.ExecuteTradeAsync(strategy, currentPrice, ct: _cts?.Token ?? default);
            if (result.Success && result.Record != null)
            {
                TradeExecuted?.Invoke(result.Record);
            }

            if (strategy.MaxExecutions.HasValue &&
                strategy.ExecutionCount + 1 >= strategy.MaxExecutions.Value)
            {
                await _dataService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed);
            }
        }
    }

    private async Task HandleAISignalAsync(TradingStrategy strategy, decimal currentPrice)
    {
        try
        {
            var agent = _agentFactory.CreateAgent();
            var prompt = $"""
                分析交易对 {strategy.Symbol}，当前价格 {currentPrice}。
                策略配置: {strategy.CustomParams ?? "无"}
                请评估是否应该执行 {strategy.Side} 操作，数量 {strategy.Quantity}。
                如果决定交易，请调用 PlaceOrder 工具执行。
                """;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, prompt)
            };

            await _dataService.UpdateStrategyTriggeredAsync(strategy.Id);

            var response = await agent.RunAsync(messages, session: null, options: null,
                cancellationToken: _cts?.Token ?? default);
            _logger.LogDebug("TradingAgent 响应: {Content}", response.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 信号策略执行失败: {StrategyId}", strategy.Id);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _evaluationLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
