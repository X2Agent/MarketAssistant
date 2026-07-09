using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安API鉴权配置（运行时快照，由 BinanceAuthService 每次从加密存储动态读取）
/// </summary>
public class BinanceAuthConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// 请求有效期窗口（毫秒，默认5000ms，最大60000ms）
    /// </summary>
    public long RecvWindow { get; set; } = 5000;
}

/// <summary>
/// 币安API鉴权服务抽象，支持现货/合约、实盘/Testnet 多套密钥隔离。
/// </summary>
public interface IBinanceAuthService
{
    /// <summary>
    /// API 密钥是否已配置
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// 为请求参数添加签名（URL query string格式），并自动与币安服务器时间同步以避免 -1021 错误。
    /// </summary>
    Task<string> SignQueryStringAsync(string queryString, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为HttpClient请求添加必要的Headers
    /// </summary>
    void AddAuthHeaders(HttpRequestMessage request);
}

/// <summary>
/// 币安API鉴权服务（HMAC-SHA256签名）
/// 通过 ITradingCredentialStore 按交易模式读取加密存储的密钥，
/// 支持现货实盘/Testnet/Demo、合约实盘/Testnet 共用同一实现。
/// 签名时自动与币安服务器时间同步，避免本地系统时间偏差导致 -1021 错误。
/// </summary>
public sealed class BinanceAuthService : IBinanceAuthService
{
    private const long RecvWindow = 5000;

    /// <summary>
    /// 服务器时间偏移量缓存有效期。偏移量变化缓慢，30分钟刷新一次足够。
    /// </summary>
    private static readonly TimeSpan ServerTimeSyncInterval = TimeSpan.FromMinutes(30);

    private readonly ITradingCredentialStore _credentialStore;
    private readonly CryptoTradingMode _mode;
    private readonly string _keyName;

    /// <summary>
    /// 用于调用 /api/v3/time 或 /fapi/v1/time 同步服务器时间的 HttpClient 工厂。
    /// 为 null 时降级到本地时间（仅适合测试或受控环境）。
    /// </summary>
    private readonly IHttpClientFactory? _httpClientFactory;

    /// <summary>时间同步使用的 HttpClient 名称（如 "Binance" / "BinanceSpotTestnet"）</summary>
    private readonly string? _httpClientName;

    /// <summary>时间同步端点（现货 "/api/v3/time"，合约 "/fapi/v1/time"）</summary>
    private readonly string? _timeEndpoint;

    /// <summary>服务器时间相对本地时间的偏移量（毫秒），正值表示本地慢于服务器</summary>
    private long _serverTimeOffsetMs;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly ILogger? _logger;

    /// <param name="credentialStore">交易凭证加密存储</param>
    /// <param name="mode">交易模式，决定从存储中读取哪套密钥</param>
    /// <param name="keyName">密钥名称，用于错误提示（如 "Binance"、"Binance Spot Testnet"）</param>
    /// <param name="httpClientFactory">用于服务器时间同步的 HttpClient 工厂（可选）</param>
    /// <param name="httpClientName">时间同步使用的 HttpClient 名称（可选，与账户服务一致）</param>
    /// <param name="timeEndpoint">时间同步端点：现货 "/api/v3/time"，合约 "/fapi/v1/time"（可选）</param>
    /// <param name="logger">日志器（可选）</param>
    public BinanceAuthService(
        ITradingCredentialStore credentialStore,
        CryptoTradingMode mode,
        string keyName,
        IHttpClientFactory? httpClientFactory = null,
        string? httpClientName = null,
        string? timeEndpoint = null,
        ILogger? logger = null)
    {
        _credentialStore = credentialStore;
        _mode = mode;
        _keyName = keyName;
        _httpClientFactory = httpClientFactory;
        _httpClientName = httpClientName;
        _timeEndpoint = timeEndpoint;
        _logger = logger;
    }

    private BinanceAuthConfig CurrentConfig
    {
        get
        {
            var (apiKey, secretKey) = _credentialStore.GetCredentials(_mode);
            return new BinanceAuthConfig
            {
                ApiKey = apiKey,
                SecretKey = secretKey,
                RecvWindow = RecvWindow
            };
        }
    }

    /// <inheritdoc />
    public bool IsConfigured
    {
        get
        {
            var cfg = CurrentConfig;
            return !string.IsNullOrEmpty(cfg.ApiKey) && !string.IsNullOrEmpty(cfg.SecretKey);
        }
    }

    /// <inheritdoc />
    public async Task<string> SignQueryStringAsync(string queryString, CancellationToken cancellationToken = default)
    {
        var config = CurrentConfig;
        EnsureConfigured(config);

        var timestamp = await GetAdjustedTimestampAsync(cancellationToken);
        var payload = string.IsNullOrEmpty(queryString)
            ? $"timestamp={timestamp}"
            : $"{queryString}&timestamp={timestamp}";

        if (config.RecvWindow != 5000)
        {
            payload += $"&recvWindow={config.RecvWindow}";
        }

        var signature = SignPayload(payload, config.SecretKey);
        return $"{payload}&signature={signature}";
    }

    /// <inheritdoc />
    public void AddAuthHeaders(HttpRequestMessage request)
    {
        var config = CurrentConfig;
        EnsureConfigured(config);
        request.Headers.Add("X-MBX-APIKEY", config.ApiKey);
    }

    /// <summary>
    /// 获取与币安服务器同步后的当前时间戳（毫秒）。
    /// 若未配置时间同步参数或同步失败，降级到本地时间。
    /// </summary>
    private async Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken)
    {
        // 未配置时间同步参数，直接用本地时间
        if (_httpClientFactory is null || string.IsNullOrEmpty(_httpClientName) || string.IsNullOrEmpty(_timeEndpoint))
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // 缓存未过期，直接用缓存的偏移量
        if (DateTime.UtcNow - _lastSyncTime < ServerTimeSyncInterval && _lastSyncTime != DateTime.MinValue)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Interlocked.Read(ref _serverTimeOffsetMs);
        }

        // 同步服务器时间（仅一个线程执行，其他线程等待并复用结果）
        await SynchronizeServerTimeAsync(cancellationToken);
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Interlocked.Read(ref _serverTimeOffsetMs);
    }

    /// <summary>
    /// 调用币安 /api/v3/time 或 /fapi/v1/time 端点，计算服务器时间相对本地时间的偏移量。
    /// 同步失败不抛异常，降级到偏移量 0（本地时间），避免阻塞签名流程。
    /// </summary>
    private async Task SynchronizeServerTimeAsync(CancellationToken cancellationToken)
    {
        // 双检锁：进入锁后再检查一次，避免多个等待线程重复同步
        if (DateTime.UtcNow - _lastSyncTime < ServerTimeSyncInterval && _lastSyncTime != DateTime.MinValue)
        {
            return;
        }

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _lastSyncTime < ServerTimeSyncInterval && _lastSyncTime != DateTime.MinValue)
            {
                return;
            }

            using var httpClient = _httpClientFactory!.CreateClient(_httpClientName!);
            // 在请求前后各取一次本地时间，取中点抵消网络往返耗时
            var localBefore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var response = await httpClient.GetAsync(_timeEndpoint!, cancellationToken);
            var localAfter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("同步{KeyName}服务器时间失败：HTTP {StatusCode}，降级使用本地时间",
                    _keyName, (int)response.StatusCode);
                return;
            }

            var timeResult = await response.Content.ReadFromJsonAsync<BinanceServerTime>(cancellationToken);
            if (timeResult is null || timeResult.ServerTime <= 0)
            {
                _logger?.LogWarning("解析{KeyName}服务器时间响应失败，降级使用本地时间", _keyName);
                return;
            }

            // 偏移量 = 服务器时间 - 本地中点时间
            var localMidpoint = (localBefore + localAfter) / 2;
            var offset = timeResult.ServerTime - localMidpoint;

            Interlocked.Exchange(ref _serverTimeOffsetMs, offset);
            _lastSyncTime = DateTime.UtcNow;

            _logger?.LogInformation("已同步{KeyName}服务器时间，偏移量 {Offset}ms", _keyName, offset);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 同步失败不应阻塞签名流程，降级到本地时间（偏移量保持 0 或上一次值）
            _logger?.LogWarning(ex, "同步{KeyName}服务器时间异常，降级使用本地时间", _keyName);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// HMAC-SHA256 签名核心算法（供所有币安鉴权服务复用）
    /// </summary>
    internal static string SignPayload(string payload, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private void EnsureConfigured(BinanceAuthConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException($"{_keyName} API Key 未配置，请在交易页面的 API 密钥配置中设置");

        if (string.IsNullOrEmpty(config.SecretKey))
            throw new InvalidOperationException($"{_keyName} Secret Key 未配置，请在交易页面的 API 密钥配置中设置");
    }

    /// <summary>
    /// 币安服务器时间响应（/api/v3/time 与 /fapi/v1/time 通用结构）
    /// </summary>
    private sealed class BinanceServerTime
    {
        [System.Text.Json.Serialization.JsonPropertyName("serverTime")]
        public long ServerTime { get; set; }
    }
}
