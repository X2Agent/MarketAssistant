using System.Collections.Concurrent;
using System.Threading.Channels;
using MarketAssistant.Agents.Trading;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Data;
using MarketAssistant.Trading.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 后台市场监控器，订阅实时价格并根据策略触发交易。
/// 使用 Channel 缓冲价格更新，按顺序处理每个 tick。
/// </summary>
public class MarketMonitor : IDisposable
{
    private readonly BinanceWebSocketService _webSocketService;
    private readonly StrategyEngine _strategyEngine;
    private readonly TradeExecutor _tradeExecutor;
    private readonly ITradingAgentFactory _agentFactory;
    private readonly TradingDataService _dataService;
    private readonly ILogger<MarketMonitor> _logger;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private Task? _consumerTask;

    private readonly Channel<(string Symbol, decimal Price)> _priceChannel =
        Channel.CreateBounded<(string, decimal)>(new BoundedChannelOptions(1000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _strategyLocks = new();

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
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_isRunning)
                return;

            _cts = new CancellationTokenSource();
            _isRunning = true;

            var activeStrategies = await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active);
            var instrumentSymbols = activeStrategies
                .Select(s => s.Symbol.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (instrumentSymbols.Count > 0)
                await _webSocketService.SubscribeAsync(instrumentSymbols);

            _webSocketService.PriceUpdated += OnPriceUpdated;
            _consumerTask = Task.Run(() => ConsumePriceUpdatesAsync(_cts.Token));

            _logger.LogInformation("MarketMonitor 已启动，监控 {Count} 个交易标的", instrumentSymbols.Count);
            StatusChanged?.Invoke(true);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 停止后台监控
    /// </summary>
    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (!_isRunning)
                return;

            _webSocketService.PriceUpdated -= OnPriceUpdated;
            _cts?.Cancel();

            if (_consumerTask != null)
            {
                try { await _consumerTask; }
                catch (OperationCanceledException) { }
            }

            await _webSocketService.UnsubscribeAllAsync();

            _isRunning = false;
            _logger.LogInformation("MarketMonitor 已停止");
            StatusChanged?.Invoke(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 刷新监控的交易标的列表（策略增减后调用）
    /// </summary>
    public async Task RefreshSubscriptionsAsync()
    {
        if (!_isRunning)
            return;

        var activeStrategies = await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active);
        var newSymbols = activeStrategies
            .Select(s => s.Symbol.ToLowerInvariant())
            .Distinct()
            .ToHashSet();

        await _webSocketService.UnsubscribeAllAsync();

        if (newSymbols.Count > 0)
            await _webSocketService.SubscribeAsync(newSymbols.ToList());

        _logger.LogInformation("已刷新监控列表: {Count} 个交易标的", newSymbols.Count);
    }

    private void OnPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        _priceChannel.Writer.TryWrite((symbol, lastPrice));
    }

    /// <summary>
    /// Channel 消费者：顺序评估策略，异步执行触发的交易（每策略独立锁）
    /// </summary>
    private async Task ConsumePriceUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (symbol, price) in _priceChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var triggered = await _strategyEngine.EvaluateStrategiesAsync(symbol, price, ct);
                    foreach (var strategy in triggered)
                        _ = ExecuteWithStrategyLockAsync(strategy, price, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "价格更新处理异常: {Symbol}", symbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("价格消费者已取消");
        }
    }

    /// <summary>
    /// 带策略级锁的异步执行，防止同一策略并发触发
    /// </summary>
    private async Task ExecuteWithStrategyLockAsync(
        TradingStrategy strategy, decimal price, CancellationToken ct)
    {
        var strategyLock = _strategyLocks.GetOrAdd(strategy.Id, _ => new SemaphoreSlim(1, 1));
        if (!await strategyLock.WaitAsync(0, ct))
        {
            _logger.LogDebug("策略 {Id} 正在执行中，跳过本次触发", strategy.Id);
            return;
        }

        try
        {
            await HandleTriggeredStrategyAsync(strategy, price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "策略执行异常: {StrategyId}", strategy.Id);
        }
        finally
        {
            strategyLock.Release();
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
            var result = await _tradeExecutor.ExecuteTradeAsync(
                strategy, currentPrice, ct: _cts?.Token ?? default);

            if (result.Success && result.Record != null)
                TradeExecuted?.Invoke(result.Record);

            await CheckStrategyCompletionAsync(strategy);
        }
    }

    private async Task HandleAISignalAsync(TradingStrategy strategy, decimal currentPrice)
    {
        try
        {
            TradingContext.CurrentStrategyId = strategy.Id;

            var priorRecords = await _dataService.GetRecordsByStrategyAsync(strategy.Id, _cts?.Token ?? default)
                .ConfigureAwait(false);
            var recentSummary = priorRecords.Count == 0
                ? "（该策略尚无成交记录）"
                : string.Join("\n", priorRecords.Take(5).Select(r =>
                    $"{r.CreatedAt:u} {r.Side} 成交量:{r.ExecutedQty} 价:{r.ExecutedPrice} {r.Status}"));

            var agent = _agentFactory.CreateAgent();
            var prompt = $"""
                分析交易标的 {strategy.Symbol}，当前价格 {currentPrice}。
                策略配置: {strategy.CustomParams ?? "无"}
                近期该策略成交摘要（最多 5 笔，按时间倒序）:
                {recentSummary}
                请评估是否应该执行 {strategy.Side} 操作，数量 {strategy.Quantity}。
                如果决定交易，请调用 PlaceOrder 工具执行。
                如果决定不交易，请说明理由。
                """;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, prompt)
            };

            var response = await agent.RunAsync(messages, session: null, options: null,
                cancellationToken: _cts?.Token ?? default);
            _logger.LogDebug("TradingAgent 响应: {Content}", response.Text);

            // 只在 Agent 实际执行了交易后才更新触发计数
            var recentRecords = await _dataService.GetRecordsByStrategyAsync(strategy.Id);
            var hasNewTrade = recentRecords.Any(r =>
                r.CreatedAt > (strategy.LastTriggeredAt ?? DateTime.MinValue));

            if (hasNewTrade)
            {
                await _dataService.UpdateStrategyTriggeredAsync(strategy.Id);
                await CheckStrategyCompletionAsync(strategy);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 信号策略执行失败: {StrategyId}", strategy.Id);
        }
        finally
        {
            TradingContext.CurrentStrategyId = null;
        }
    }

    private async Task CheckStrategyCompletionAsync(TradingStrategy strategy)
    {
        if (!strategy.MaxExecutions.HasValue)
            return;

        var updated = await _dataService.GetStrategyAsync(strategy.Id);
        if (updated != null && updated.ExecutionCount >= updated.MaxExecutions!.Value)
        {
            await _dataService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed);
            _strategyEngine.ClearPeakPrice(strategy.Id);
            _strategyLocks.TryRemove(strategy.Id, out _);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _priceChannel.Writer.TryComplete();
        foreach (var kvp in _strategyLocks)
            kvp.Value.Dispose();
        _strategyLocks.Clear();
        _lifecycleLock.Dispose();

        TradeExecuted = null;
        StatusChanged = null;
        _webSocketService.PriceUpdated -= OnPriceUpdated;

        GC.SuppressFinalize(this);
    }
}
