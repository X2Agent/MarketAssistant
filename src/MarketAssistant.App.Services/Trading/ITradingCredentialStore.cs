using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易 API 密钥加密存储接口，独立于 UserSetting，按交易模式隔离密钥。
/// </summary>
public interface ITradingCredentialStore
{
    /// <summary>
    /// 获取指定交易模式的 API 密钥
    /// </summary>
    (string ApiKey, string SecretKey) GetCredentials(CryptoTradingMode mode);

    /// <summary>
    /// 设置指定交易模式的 API 密钥并加密持久化
    /// </summary>
    void SetCredentials(CryptoTradingMode mode, string apiKey, string secretKey);

    /// <summary>
    /// 指定交易模式是否已配置密钥
    /// </summary>
    bool IsConfigured(CryptoTradingMode mode);

    /// <summary>
    /// 清除指定交易模式的密钥
    /// </summary>
    void ClearCredentials(CryptoTradingMode mode);
}
