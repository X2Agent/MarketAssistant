using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易 API 密钥加密存储实现。使用 AES-GCM 加密，密钥从机器信息派生，
/// 存储在独立文件中，与 UserSetting 完全隔离。
/// 首次加载时自动从 UserSetting 迁移已有密钥（仅迁移一次）。
/// </summary>
public sealed class TradingCredentialStore : ITradingCredentialStore
{
    private const string CredentialFileName = "trading-credentials.dat";
    private const string MigrationMarkerFileName = "trading-credentials.migrated";
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int KeySize = 256 / 8;
    private const int TagSize = 16;
    private const int Iterations = 100_000;

    private static readonly string FilePath = Path.Combine(FileSystem.AppDataDirectory, CredentialFileName);
    private static readonly string MigrationMarkerPath = Path.Combine(FileSystem.AppDataDirectory, MigrationMarkerFileName);

    private readonly ILogger<TradingCredentialStore> _logger;
    private readonly object _fileLock = new();
    private Dictionary<CryptoTradingMode, CredentialEntry> _cache;

    public TradingCredentialStore(
        IUserSettingService userSettingService,
        ILogger<TradingCredentialStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = Load();

        // 首次启动时从 UserSetting 迁移已有密钥
        MigrateFromUserSettingIfNeeded(userSettingService.CurrentSetting);
    }

    public (string ApiKey, string SecretKey) GetCredentials(CryptoTradingMode mode)
    {
        if (_cache.TryGetValue(mode, out var entry))
            return (entry.ApiKey, entry.SecretKey);
        return (string.Empty, string.Empty);
    }

    public void SetCredentials(CryptoTradingMode mode, string apiKey, string secretKey)
    {
        _cache[mode] = new CredentialEntry(apiKey, secretKey);
        Save();
    }

    public bool IsConfigured(CryptoTradingMode mode)
    {
        if (!_cache.TryGetValue(mode, out var entry))
            return false;
        return !string.IsNullOrEmpty(entry.ApiKey) && !string.IsNullOrEmpty(entry.SecretKey);
    }

    public void ClearCredentials(CryptoTradingMode mode)
    {
        if (_cache.Remove(mode))
            Save();
    }

    /// <summary>
    /// 首次启动时从 UserSetting 迁移已有 Binance 密钥到加密存储。
    /// 通过标记文件确保只执行一次。
    /// </summary>
    private void MigrateFromUserSettingIfNeeded(UserSetting setting)
    {
        if (File.Exists(MigrationMarkerPath))
            return;

        try
        {
            var migrated = false;

            // 实盘现货/合约共用同一套密钥
            if (!string.IsNullOrEmpty(setting.BinanceApiKey) && !string.IsNullOrEmpty(setting.BinanceSecretKey))
            {
                _cache[CryptoTradingMode.LiveSpot] = new CredentialEntry(setting.BinanceApiKey, setting.BinanceSecretKey);
                _cache[CryptoTradingMode.LiveFutures] = new CredentialEntry(setting.BinanceApiKey, setting.BinanceSecretKey);
                migrated = true;
            }

            // Futures Testnet
            if (!string.IsNullOrEmpty(setting.BinanceFuturesTestnetApiKey) && !string.IsNullOrEmpty(setting.BinanceFuturesTestnetSecretKey))
            {
                _cache[CryptoTradingMode.BinanceFuturesTestnet] = new CredentialEntry(setting.BinanceFuturesTestnetApiKey, setting.BinanceFuturesTestnetSecretKey);
                migrated = true;
            }

            if (migrated)
            {
                Save();
                _logger.LogInformation("已从 UserSetting 迁移 {Count} 组交易 API 密钥到加密存储", _cache.Count);
            }

            // 写入迁移标记，无论是否找到密钥都标记为已处理
            File.WriteAllText(MigrationMarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 UserSetting 迁移交易密钥失败，将在下次启动时重试");
        }
    }

    private Dictionary<CryptoTradingMode, CredentialEntry> Load()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new Dictionary<CryptoTradingMode, CredentialEntry>();

                var bytes = File.ReadAllBytes(FilePath);
                return Decrypt(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载交易凭证失败，使用空配置");
                return new Dictionary<CryptoTradingMode, CredentialEntry>();
            }
        }
    }

    private void Save()
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache);
                var encrypted = Encrypt(json);
                File.WriteAllBytes(FilePath, encrypted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存交易凭证失败");
            }
        }
    }

    private byte[] Encrypt(string plaintext)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(salt);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // [salt(32)][nonce(12)][tag(16)][ciphertext(N)]
        var result = new byte[SaltSize + NonceSize + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, result, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, result, SaltSize + NonceSize, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, result, SaltSize + NonceSize + tag.Length, ciphertext.Length);
        return result;
    }

    private Dictionary<CryptoTradingMode, CredentialEntry> Decrypt(byte[] data)
    {
        if (data.Length < SaltSize + NonceSize + TagSize)
            return new Dictionary<CryptoTradingMode, CredentialEntry>();

        var salt = new byte[SaltSize];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[data.Length - SaltSize - NonceSize - TagSize];

        Buffer.BlockCopy(data, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(data, SaltSize, nonce, 0, NonceSize);
        Buffer.BlockCopy(data, SaltSize + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(data, SaltSize + NonceSize + TagSize, ciphertext, 0, ciphertext.Length);

        var key = DeriveKey(salt);
        var plaintextBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        var json = Encoding.UTF8.GetString(plaintextBytes);
        return JsonSerializer.Deserialize<Dictionary<CryptoTradingMode, CredentialEntry>>(json)
               ?? new Dictionary<CryptoTradingMode, CredentialEntry>();
    }

    /// <summary>
    /// 从机器名、用户名和应用标识派生 AES 密钥，确保不同机器/用户的密钥不同。
    /// </summary>
    private static byte[] DeriveKey(byte[] salt)
    {
        var identity = $"{Environment.MachineName}|{Environment.UserName}|MarketAssistant.Trading";
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(identity), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    private sealed class CredentialEntry
    {
        public string ApiKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;

        public CredentialEntry() { }
        public CredentialEntry(string apiKey, string secretKey)
        {
            ApiKey = apiKey;
            SecretKey = secretKey;
        }
    }
}
