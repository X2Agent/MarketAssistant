using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.DataProviders;

/// <summary>
/// Binance WebSocket 实时行情服务，通过 mini-ticker 推送价格更新
/// </summary>
public sealed class BinanceWebSocketService : IAsyncDisposable, IDisposable
{
    private const string WsBaseUrl = "wss://stream.binance.com:9443/stream?streams=";
    private const int ReconnectDelayMs = 5000;
    private readonly ILogger<BinanceWebSocketService> _logger;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private int _retryScheduled;

    /// <summary>
    /// 各订阅方（价格告警、交易监控、收藏页、资产详情）独立维护的交易对集合。
    /// 实际订阅集为所有订阅方的并集，任一订阅方退订只影响自己的集合，
    /// 避免某模块取消订阅时把其他模块的行情订阅一并清空。
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _subscriberSymbols = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前实际连接的交易对集合（所有订阅方并集，小写），供重连使用。
    /// </summary>
    private HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _lock = new();

    /// <summary>
    /// 连接生命周期闸门：订阅变更、断开、重连必须整体串行执行。
    /// UI 线程（收藏页/资产页）与后台线程（价格告警/交易监控）可能并发触发，
    /// 无闸门时两个并发重连会互相覆盖 _ws，导致已连接 socket 成为孤儿并重复推送。
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    /// 收到价格更新时触发，参数为 (symbol, lastPrice, priceChangePercent)
    /// </summary>
    public event Action<string, decimal, decimal>? PriceUpdated;

    public BinanceWebSocketService(ILogger<BinanceWebSocketService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 以指定订阅方身份订阅实时行情。同一订阅方重复调用会整体替换其交易对集合，
    /// 因此调用方应在此传入该订阅方当前需要的完整集合。
    /// </summary>
    /// <param name="subscriberKey">订阅方标识（见 <see cref="WebSocketSubscriberKeys"/>）</param>
    /// <param name="symbols">Binance 格式的交易对列表，如 ["BTCUSDT","ETHUSDT"]</param>
    public async Task SubscribeAsync(string subscriberKey, IEnumerable<string> symbols)
    {
        var symbolSet = symbols.Select(s => s.ToLowerInvariant()).Distinct().ToHashSet();

        await _lifecycleGate.WaitAsync();
        try
        {
            bool changed;
            lock (_lock)
            {
                _subscriberSymbols[subscriberKey] = symbolSet;
                changed = RecomputeSubscribedSymbolsLocked();
            }

            if (!changed) return;
            await ApplySubscriptionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance WebSocket 订阅连接失败，{Delay}ms 后自动重试", ReconnectDelayMs);
            ScheduleConnectRetry();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// 取消指定订阅方的全部订阅。仅移除该订阅方自身的交易对集合，
    /// 不会影响其他订阅方的订阅；并集为空时断开连接。
    /// </summary>
    public async Task UnsubscribeAllAsync(string subscriberKey)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            bool changed;
            lock (_lock)
            {
                changed = _subscriberSymbols.Remove(subscriberKey) && RecomputeSubscribedSymbolsLocked();
            }

            if (!changed) return;
            await ApplySubscriptionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance WebSocket 退订重连失败，{Delay}ms 后自动重试", ReconnectDelayMs);
            ScheduleConnectRetry();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// 在 _lock 内重算所有订阅方的并集；返回并集是否发生变化，调用方据此决定是否重连。
    /// </summary>
    private bool RecomputeSubscribedSymbolsLocked()
    {
        var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbols in _subscriberSymbols.Values)
            union.UnionWith(symbols);

        if (union.SetEquals(_subscribedSymbols))
            return false;

        _subscribedSymbols = union;
        return true;
    }

    /// <summary>
    /// 按当前并集重建连接（先断开旧连接再连接新集合）。调用方必须已持有 <see cref="_lifecycleGate"/>。
    /// 连接失败时抛出异常（资源已清理），由调用方决定重试策略。
    /// </summary>
    private async Task ApplySubscriptionAsync()
    {
        await DisconnectCoreAsync();

        HashSet<string> symbols;
        lock (_lock)
        {
            symbols = [.. _subscribedSymbols];
        }

        if (symbols.Count == 0) return;

        var streams = string.Join("/", symbols.Select(s => $"{s}@miniTicker"));
        var url = WsBaseUrl + streams;

        var cts = new CancellationTokenSource();
        var ws = new ClientWebSocket();
        try
        {
            _logger.LogInformation("连接 Binance WebSocket，订阅 {Count} 个交易对", symbols.Count);
            await ws.ConnectAsync(new Uri(url), cts.Token);
        }
        catch
        {
            cts.Dispose();
            ws.Dispose();
            throw;
        }

        // 连接成功后才落字段，失败路径不残留半初始化状态
        _ws = ws;
        _cts = cts;
        _ = ReceiveLoopAsync(ws, cts.Token);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        // Binance 组合流消息可能分片到达，使用动态缓冲区拼接完整消息
        var buffer = new byte[8192];
        using var messageStream = new MemoryStream();

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                messageStream.SetLength(0);

                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("Binance WebSocket 服务端关闭连接");
                        return;
                    }
                    messageStream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);
                ProcessMessage(json);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消（退订或释放）
        }
        catch (Exception ex)
        {
            // 网络断开、socket 被并发释放等均视为断线；仅当本连接仍是当前连接时才重连，
            // 避免旧循环把新连接顶掉
            _logger.LogWarning(ex, "Binance WebSocket 断开，将在 {Delay}ms 后重连", ReconnectDelayMs);
            await ScheduleReconnectIfCurrentAsync(ws, ct);
        }
    }

    /// <summary>
    /// 延迟后重连，但仅当 <paramref name="ws"/> 仍是当前连接时执行；
    /// 期间若发生过退订/重订阅/释放则放弃，避免覆盖新状态。
    /// </summary>
    private async Task ScheduleReconnectIfCurrentAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            // 使用 ct 而非 CancellationToken.None，确保应用关闭时重连延迟可被取消
            await Task.Delay(ReconnectDelayMs, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        await _lifecycleGate.WaitAsync();
        try
        {
            if (_disposed || _ws != ws) return;
            await ApplySubscriptionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance WebSocket 重连失败，{Delay}ms 后自动重试", ReconnectDelayMs);
            ScheduleConnectRetry();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// 连接失败后的退避重试（去重排队）。成功连接或并集清空后自动退出。
    /// </summary>
    private void ScheduleConnectRetry()
    {
        if (Interlocked.Exchange(ref _retryScheduled, 1) == 1) return;
        _ = ConnectRetryLoopAsync();
    }

    private async Task ConnectRetryLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(ReconnectDelayMs);

                await _lifecycleGate.WaitAsync();
                bool shouldContinue;
                try
                {
                    if (_disposed) return;
                    lock (_lock)
                    {
                        if (_subscribedSymbols.Count == 0) return;
                    }

                    await ApplySubscriptionAsync();
                    shouldContinue = false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Binance WebSocket 重试连接失败，将继续重试");
                    shouldContinue = true;
                }
                finally
                {
                    Interlocked.Exchange(ref _retryScheduled, 0);
                    _lifecycleGate.Release();
                }

                if (!shouldContinue) return;
            }
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _retryScheduled, 0);
            _logger.LogWarning(ex, "Binance WebSocket 重连循环终止");
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data)) return;

            var symbol = data.GetProperty("s").GetString();
            var lastPriceStr = data.GetProperty("c").GetString();
            var openPriceStr = data.GetProperty("o").GetString();

            if (symbol == null || lastPriceStr == null || openPriceStr == null) return;

            var lastPrice = decimal.Parse(lastPriceStr, CultureInfo.InvariantCulture);
            var openPrice = decimal.Parse(openPriceStr, CultureInfo.InvariantCulture);
            var changePercent = openPrice > 0
                ? Math.Round((lastPrice - openPrice) / openPrice * 100, 2)
                : 0m;

            PriceUpdated?.Invoke(symbol.ToUpperInvariant(), lastPrice, changePercent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 WebSocket 消息失败");
        }
    }

    /// <summary>
    /// 断开并清理当前连接。调用方必须已持有 <see cref="_lifecycleGate"/>。
    /// 先清字段再关闭，确保在飞的重连检查立即把本连接视为过期。
    /// </summary>
    private async Task DisconnectCoreAsync()
    {
        var cts = _cts;
        _cts = null;
        var ws = _ws;
        _ws = null;

        if (cts != null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (ws != null)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "unsubscribe", CancellationToken.None);
                }
                catch
                {
                    // 忽略关闭异常
                }
            }
            ws.Dispose();
        }
    }

    /// <summary>
    /// 异步释放资源，避免在 UI 线程上同步等待异步关闭操作造成死锁
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
        _lifecycleGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // 同步释放底层 WebSocket，不等待异步 CloseOutputAsync；
        // 在飞的接收循环会因 _ws 不再匹配而放弃重连
        _ws?.Dispose();
        _ws = null;

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 预定义订阅方标识，保证各模块退订时使用与订阅时一致的 key。
/// </summary>
public static class WebSocketSubscriberKeys
{
    public const string PriceAlerts = "price-alerts";
    public const string MarketMonitor = "market-monitor";
    public const string Favorites = "favorites";
    public const string AssetDetail = "asset-detail";
}
