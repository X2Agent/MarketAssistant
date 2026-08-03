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
    private readonly ILogger<BinanceWebSocketService> _logger;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 各订阅方（价格告警、交易监控、收藏页、资产详情）独立维护的交易对集合。
    /// 实际订阅集为所有订阅方的并集，任一订阅方退订只影响自己的集合，
    /// 避免某模块取消订阅时把其他模块的行情订阅一并清空。
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _subscriberSymbols = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前实际连接的交易对集合（所有订阅方并集，小写），供 ReconnectAsync 使用。
    /// </summary>
    private HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _lock = new();

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

        bool changed;
        lock (_lock)
        {
            _subscriberSymbols[subscriberKey] = symbolSet;
            changed = RecomputeSubscribedSymbolsLocked();
        }

        if (!changed) return;

        if (_subscribedSymbols.Count == 0)
            await DisconnectAsync();
        else
            await ReconnectAsync();
    }

    /// <summary>
    /// 取消指定订阅方的全部订阅。仅移除该订阅方自身的交易对集合，
    /// 不会影响其他订阅方的订阅；并集为空时断开连接。
    /// </summary>
    public async Task UnsubscribeAllAsync(string subscriberKey)
    {
        bool changed;
        lock (_lock)
        {
            changed = _subscriberSymbols.Remove(subscriberKey) && RecomputeSubscribedSymbolsLocked();
        }

        if (!changed) return;

        if (_subscribedSymbols.Count == 0)
            await DisconnectAsync();
        else
            await ReconnectAsync();
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

    private async Task ReconnectAsync()
    {
        await DisconnectAsync();

        List<string> symbols;
        lock (_lock)
        {
            symbols = [.. _subscribedSymbols];
        }

        if (symbols.Count == 0) return;

        var streams = string.Join("/", symbols.Select(s => $"{s}@miniTicker"));
        var url = WsBaseUrl + streams;

        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        try
        {
            _logger.LogInformation("连接 Binance WebSocket，订阅 {Count} 个交易对", symbols.Count);
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            _ = ReceiveLoopAsync(_ws, _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance WebSocket 连接失败");
        }
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
            // 正常取消
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "Binance WebSocket 断开，将在 5 秒后重连");
            // 使用 ct 而非 CancellationToken.None，确保应用关闭时重连延迟可被取消
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!ct.IsCancellationRequested)
                _ = ReconnectAsync();
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

    private async Task DisconnectAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "unsubscribe", CancellationToken.None);
                }
                catch
                {
                    // 忽略关闭异常
                }
            }
            _ws.Dispose();
            _ws = null;
        }
    }

    /// <summary>
    /// 异步释放资源，避免在 UI 线程上同步等待异步关闭操作造成死锁
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // 同步释放底层 WebSocket，不等待异步 CloseOutputAsync
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
