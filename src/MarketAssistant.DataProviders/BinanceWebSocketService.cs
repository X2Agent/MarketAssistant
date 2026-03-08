using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Data;

/// <summary>
/// Binance WebSocket 实时行情服务，通过 mini-ticker 推送价格更新
/// </summary>
public sealed class BinanceWebSocketService : IDisposable
{
    private const string WsBaseUrl = "wss://stream.binance.com:9443/stream?streams=";
    private readonly ILogger<BinanceWebSocketService> _logger;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
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
    /// 订阅指定交易对的实时行情
    /// </summary>
    /// <param name="symbols">Binance 格式的交易对列表，如 ["BTCUSDT","ETHUSDT"]</param>
    public async Task SubscribeAsync(IEnumerable<string> symbols)
    {
        var symbolList = symbols.Select(s => s.ToLowerInvariant()).Distinct().ToList();
        if (symbolList.Count == 0) return;

        lock (_lock)
        {
            foreach (var s in symbolList)
                _subscribedSymbols.Add(s);
        }

        await ReconnectAsync();
    }

    /// <summary>
    /// 取消订阅并断开连接
    /// </summary>
    public async Task UnsubscribeAllAsync()
    {
        lock (_lock)
        {
            _subscribedSymbols.Clear();
        }

        await DisconnectAsync();
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
        var buffer = new byte[4096];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Binance WebSocket 服务端关闭连接");
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
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
            await Task.Delay(5000, CancellationToken.None);
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

            var lastPrice = decimal.Parse(lastPriceStr);
            var openPrice = decimal.Parse(openPriceStr);
            var changePercent = openPrice > 0
                ? Math.Round((lastPrice - openPrice) / openPrice * 100, 2)
                : 0m;

            PriceUpdated?.Invoke(symbol.ToUpperInvariant(), lastPrice, changePercent);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "解析 WebSocket 消息失败");
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

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}
