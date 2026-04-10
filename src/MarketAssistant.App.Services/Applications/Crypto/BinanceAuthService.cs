using System.Security.Cryptography;
using System.Text;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安API鉴权配置（运行时快照，由 BinanceAuthService 每次从设置动态读取）
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
/// 币安API鉴权服务（HMAC-SHA256签名）
/// 每次操作时从 IUserSettingService 动态读取密钥，以支持运行时更改
/// </summary>
public class BinanceAuthService
{
    private readonly ILogger<BinanceAuthService> _logger;
    private readonly IUserSettingService _userSettingService;
    private readonly long _recvWindow;

    public BinanceAuthService(ILogger<BinanceAuthService> logger, IUserSettingService userSettingService)
    {
        _logger = logger;
        _userSettingService = userSettingService;
        _recvWindow = 5000;
    }

    private BinanceAuthConfig CurrentConfig => new()
    {
        ApiKey = _userSettingService.CurrentSetting.BinanceApiKey,
        SecretKey = _userSettingService.CurrentSetting.BinanceSecretKey,
        RecvWindow = _recvWindow
    };

    /// <summary>
    /// API 密钥是否已配置
    /// </summary>
    public bool IsConfigured
    {
        get
        {
            var cfg = CurrentConfig;
            return !string.IsNullOrEmpty(cfg.ApiKey) && !string.IsNullOrEmpty(cfg.SecretKey);
        }
    }

    /// <summary>
    /// 为请求参数添加签名（URL query string格式）
    /// </summary>
    /// <param name="queryString">已有的查询参数（不包含?），如 "symbol=BTCUSDT&amp;side=BUY"</param>
    /// <returns>添加了timestamp和signature的完整查询字符串</returns>
    public string SignQueryString(string queryString)
    {
        var config = CurrentConfig;
        EnsureConfigured(config);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = string.IsNullOrEmpty(queryString)
            ? $"timestamp={timestamp}"
            : $"{queryString}&timestamp={timestamp}";

        if (config.RecvWindow != 5000)
        {
            payload += $"&recvWindow={config.RecvWindow}";
        }

        var signature = GenerateSignature(payload, config.SecretKey);
        return $"{payload}&signature={signature}";
    }

    /// <summary>
    /// 为HttpClient请求添加必要的Headers
    /// </summary>
    public void AddAuthHeaders(HttpRequestMessage request)
    {
        var config = CurrentConfig;
        EnsureConfigured(config);
        request.Headers.Add("X-MBX-APIKEY", config.ApiKey);
    }

    private static string GenerateSignature(string payload, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static void EnsureConfigured(BinanceAuthConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Binance API Key 未配置，请在设置页面配置");

        if (string.IsNullOrEmpty(config.SecretKey))
            throw new InvalidOperationException("Binance Secret Key 未配置，请在设置页面配置");
    }
}
