using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安现货用户数据流服务：通过 ListenKey 建立 WebSocket，实时推送订单成交回报与账户余额变动。
/// 仅对现货模式（实盘现货 / 现货 Demo）生效；合约模式不启动。
/// ListenKey 无需签名，仅需 X-MBX-APIKEY 头，每 30 分钟 PUT 续期，关闭时 DELETE 释放。
/// </summary>
public sealed class BinanceUserDataStreamService : IAsyncDisposable, IDisposable
{
    private const string WsBaseUrl = "wss://stream.binance.com:9443/ws/";
    private const string ListenKeyEndpoint = "/api/v3/userDataStream";
    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromMinutes(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITradingCredentialStore _credentialStore;
    private readonly TradingEnvironmentService _environmentService;
    private readonly ILogger<BinanceUserDataStreamService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private string? _listenKey;
    private Timer? _keepaliveTimer;
    private bool _running;

    /// <summary>收到订单状态变更回报时触发。</summary>
    public event Action<ExecutionReport>? OrderUpdate;


    public BinanceUserDataStreamService(
        IHttpClientFactory httpClientFactory,
        ITradingCredentialStore credentialStore,
        TradingEnvironmentService environmentService,
        ILogger<BinanceUserDataStreamService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialStore = credentialStore;
        _environmentService = environmentService;
        _logger = logger;

        // 模式切换时若正在运行，自动重启以使用新模式密钥
        _environmentService.ModeChanged += OnModeChanged;
    }

    /// <summary>
    /// 启动现货用户数据流。非现货模式或未配置密钥时安全跳过。
    /// </summary>
    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_running)
                return;

            if (!TryGetSpotContext(out var apiKey, out var httpClientName))
            {
                _logger.LogInformation("当前非现货模式或未配置 API Key，跳过用户数据流");
                return;
            }

            _listenKey = await CreateListenKeyAsync(apiKey, httpClientName);
            if (string.IsNullOrEmpty(_listenKey))
                return;

            _cts = new CancellationTokenSource();
            _running = true;

            _keepaliveTimer = new Timer(
                _ => _ = KeepaliveAsync(apiKey, httpClientName, _cts.Token),
                null, KeepaliveInterval, KeepaliveInterval);

            _ = ConnectAndReceiveAsync(_listenKey, _cts.Token);
            _logger.LogInformation("币安现货用户数据流已启动（模式={Mode}）", _environmentService.CurrentMode);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 停止用户数据流，关闭 WebSocket 并释放 ListenKey。
    /// </summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _running = false;

            if (_keepaliveTimer != null)
            {
                await _keepaliveTimer.DisposeAsync();
                _keepaliveTimer = null;
            }

            if (_cts != null)
            {
                await _cts.CancelAsync();
                _cts.Dispose();
                _cts = null;
            }

            // 先快照到局部变量再判空：重连循环与 StopAsync 并发时，
            // 若先判字段再快照，两行之间字段可能被对方置 null 导致 NRE
            var ws = _ws;
            _ws = null;
            if (ws != null)
            {
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None);
                    }
                    catch
                    {
                        // 忽略关闭异常
                    }
                }
                ws.Dispose();
            }

            if (_listenKey != null && TryGetSpotContext(out var apiKey, out var httpClientName))
            {
                await CloseListenKeyAsync(apiKey, httpClientName, _listenKey);
            }

            _listenKey = null;
            _logger.LogInformation("币安现货用户数据流已停止");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnModeChanged(CryptoTradingMode newMode)
    {
        // 模式切换在 UI 线程触发，异步重启避免阻塞
        if (_running)
            _ = RestartAsync();
    }

    private async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    /// <summary>
    /// 获取当前现货模式的 API Key 与命名 HttpClient。非现货模式返回 false。
    /// </summary>
    private bool TryGetSpotContext(out string apiKey, out string httpClientName)
    {
        apiKey = string.Empty;
        httpClientName = _environmentService.CurrentMode switch
        {
            CryptoTradingMode.LiveSpot => "Binance",
            CryptoTradingMode.BinanceSpotDemo => "BinanceSpotDemo",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(httpClientName))
            return false;

        var (key, _) = _credentialStore.GetCredentials(_environmentService.CurrentMode);
        if (string.IsNullOrEmpty(key))
            return false;

        apiKey = key;
        return true;
    }

    private async Task<string?> CreateListenKeyAsync(string apiKey, string httpClientName)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(httpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, ListenKeyEndpoint);
            request.Headers.Add("X-MBX-APIKEY", apiKey);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            return payload.TryGetProperty("listenKey", out var key) ? key.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建 ListenKey 失败（模式={Mode}），用户数据流不可用", _environmentService.CurrentMode);
            return null;
        }
    }

    private async Task KeepaliveAsync(string apiKey, string httpClientName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_listenKey) || ct.IsCancellationRequested)
            return;

        try
        {
            var client = _httpClientFactory.CreateClient(httpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Put,
                $"{ListenKeyEndpoint}?listenKey={Uri.EscapeDataString(_listenKey)}");
            request.Headers.Add("X-MBX-APIKEY", apiKey);

            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "续期 ListenKey 失败，连接可能在 60 分钟后断开");
        }
    }

    private async Task CloseListenKeyAsync(string apiKey, string httpClientName, string listenKey)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(httpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Delete,
                $"{ListenKeyEndpoint}?listenKey={Uri.EscapeDataString(listenKey)}");
            request.Headers.Add("X-MBX-APIKEY", apiKey);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "关闭 ListenKey 失败（可忽略，服务端会自动过期）");
        }
    }

    private async Task ConnectAndReceiveAsync(string listenKey, CancellationToken ct)
    {
        var url = WsBaseUrl + listenKey;

        while (!ct.IsCancellationRequested)
        {
            // 使用局部变量持有连接，避免与 StopAsync 并发时对字段判空后字段被置 null 的竞争
            var ws = new ClientWebSocket();
            _ws = ws;
            try
            {
                _logger.LogInformation("连接币安用户数据流 WebSocket");
                await ws.ConnectAsync(new Uri(url), ct);
                await ReceiveLoopAsync(ws, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "用户数据流 WebSocket 断开，5 秒后重连");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户数据流 WebSocket 异常，5 秒后重连");
            }
            finally
            {
                ws.Dispose();
                // 仅当字段仍指向本次连接时才清空，避免误清 StopAsync 已快照或重连后新赋值的实例
                if (ReferenceEquals(_ws, ws))
                    _ws = null;
            }

            if (ct.IsCancellationRequested)
                return;

            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var messageStream = new MemoryStream();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            messageStream.SetLength(0);

            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("用户数据流服务端关闭连接");
                    return;
                }
                messageStream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);
            ProcessMessage(json);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("e", out var eventTypeProp))
                return;

            var eventType = eventTypeProp.GetString();
            switch (eventType)
            {
                case "executionReport":
                    var report = JsonSerializer.Deserialize<ExecutionReport>(json);
                    if (report != null)
                        OrderUpdate?.Invoke(report);
                    break;

            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析用户数据流消息失败");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _environmentService.ModeChanged -= OnModeChanged;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        _environmentService.ModeChanged -= OnModeChanged;
        _running = false;
        _keepaliveTimer?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        var ws = _ws;
        _ws = null;
        ws?.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
