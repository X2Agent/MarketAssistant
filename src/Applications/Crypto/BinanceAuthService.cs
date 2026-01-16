using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安API鉴权配置
/// 使用HMAC-SHA256签名方式（签名不区分大小写）
/// 文档：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api/request-security#hmac-keys
/// </summary>
public class BinanceAuthConfig
{
    /// <summary>
    /// API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// API Secret Key
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// 请求有效期窗口（毫秒，默认5000ms，最大60000ms）
    /// </summary>
    public long RecvWindow { get; set; } = 5000;
}

/// <summary>
/// 币安API鉴权服务（HMAC-SHA256签名）
/// 文档：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api/request-security#hmac-keys
/// </summary>
public class BinanceAuthService
{
    private readonly ILogger<BinanceAuthService> _logger;
    private readonly BinanceAuthConfig _config;

    public BinanceAuthService(ILogger<BinanceAuthService> logger, BinanceAuthConfig config)
    {
        _logger = logger;
        _config = config ?? throw new ArgumentNullException(nameof(config));

        ValidateConfig();
    }

    /// <summary>
    /// 为请求参数添加签名（URL query string格式）
    /// </summary>
    /// <param name="queryString">已有的查询参数（不包含?），如 "symbol=BTCUSDT&amp;side=BUY"</param>
    /// <returns>添加了timestamp和signature的完整查询字符串</returns>
    public string SignQueryString(string queryString)
    {
        // 1. 添加timestamp
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = string.IsNullOrEmpty(queryString)
            ? $"timestamp={timestamp}"
            : $"{queryString}&timestamp={timestamp}";

        // 2. 添加recvWindow（可选）
        if (_config.RecvWindow != 5000) // 只在非默认值时添加
        {
            payload += $"&recvWindow={_config.RecvWindow}";
        }

        // 3. 计算签名
        var signature = GenerateSignature(payload);

        // 4. 添加签名参数
        var signedPayload = $"{payload}&signature={signature}";

        _logger.LogDebug("已签名请求: {Payload}", signedPayload);
        return signedPayload;
    }

    /// <summary>
    /// 为HttpClient请求添加必要的Headers
    /// </summary>
    public void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-MBX-APIKEY", _config.ApiKey);
    }

    /// <summary>
    /// 生成HMAC-SHA256签名
    /// 签名不区分大小写
    /// </summary>
    private string GenerateSignature(string payload)
    {
        if (string.IsNullOrEmpty(_config.SecretKey))
        {
            throw new InvalidOperationException("HMAC签名需要配置SecretKey");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.SecretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));

        // 转换为十六进制字符串（不区分大小写）
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    #region 配置验证

    private void ValidateConfig()
    {
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            throw new ArgumentException("ApiKey不能为空", nameof(_config.ApiKey));
        }

        if (string.IsNullOrEmpty(_config.SecretKey))
        {
            throw new ArgumentException("SecretKey不能为空", nameof(_config.SecretKey));
        }

        if (_config.RecvWindow < 0 || _config.RecvWindow > 60000)
        {
            throw new ArgumentOutOfRangeException(nameof(_config.RecvWindow),
                "RecvWindow必须在0-60000毫秒之间");
        }
    }

    #endregion
}
