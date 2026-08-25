using MarketAssistant.Applications.Crypto;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading.Exchanges;

/// <summary>
/// Binance 交易所客户端工厂：按交易模式构建独立的鉴权/账户/客户端实例（P1-5）。
/// 实盘合约复用现货实盘 API Key（同一账户，需在 binance.com 开启合约权限）。
/// </summary>
public sealed class BinanceExchangeClientFactory : IExchangeClientFactory
{
    private readonly ITradingCredentialStore _credentialStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public BinanceExchangeClientFactory(
        ITradingCredentialStore credentialStore,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _credentialStore = credentialStore;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>构建指定交易模式的 Binance 客户端。</summary>
    public IExchangeClient Create(CryptoTradingMode tradingMode) => tradingMode switch
    {
        CryptoTradingMode.LiveSpot => CreateLiveSpot(),
        CryptoTradingMode.BinanceSpotDemo => CreateSpotDemo(),
        CryptoTradingMode.LiveFutures => CreateLiveFutures(),
        CryptoTradingMode.BinanceFuturesTestnet => CreateFuturesTestnet(),
        _ => throw new ArgumentOutOfRangeException(nameof(tradingMode), tradingMode, "不支持的交易模式"),
    };

    private IExchangeClient CreateLiveSpot()
    {
        var auth = CreateAuth(CryptoTradingMode.LiveSpot, "Binance", "Binance", "/api/v3/time");
        var account = new BinanceSpotAccountService(
            _httpClientFactory,
            _loggerFactory.CreateLogger<BinanceSpotAccountService>(),
            auth,
            "Binance",
            string.Empty);
        return new BinanceExchangeClient(account, "Binance");
    }

    private IExchangeClient CreateSpotDemo()
    {
        var auth = CreateAuth(CryptoTradingMode.BinanceSpotDemo, "Binance Spot Demo", "BinanceSpotDemo", "/api/v3/time");
        var account = new BinanceSpotAccountService(
            _httpClientFactory,
            _loggerFactory.CreateLogger<BinanceSpotAccountService>(),
            auth,
            "BinanceSpotDemo",
            "Demo ");
        return new BinanceExchangeClient(account, "Binance Spot Demo");
    }

    private IExchangeClient CreateLiveFutures()
    {
        // 实盘合约复用现货实盘 API Key（同一账户，需在 binance.com 开启合约权限）
        var auth = CreateAuth(CryptoTradingMode.LiveFutures, "Binance Futures", "BinanceFutures", "/fapi/v1/time");
        var account = new BinanceFuturesAccountService(
            _httpClientFactory,
            _loggerFactory.CreateLogger<BinanceFuturesAccountService>(),
            auth,
            "BinanceFutures",
            string.Empty);
        return new BinanceFuturesExchangeClient(account, "Binance Futures");
    }

    private IExchangeClient CreateFuturesTestnet()
    {
        var auth = CreateAuth(CryptoTradingMode.BinanceFuturesTestnet, "Binance Futures Testnet", "BinanceFuturesTestnet", "/fapi/v1/time");
        var account = new BinanceFuturesAccountService(
            _httpClientFactory,
            _loggerFactory.CreateLogger<BinanceFuturesAccountService>(),
            auth,
            "BinanceFuturesTestnet",
            "Testnet ");
        return new BinanceFuturesExchangeClient(account, "Binance Futures Testnet");
    }

    private BinanceAuthService CreateAuth(
        CryptoTradingMode tradingMode,
        string label,
        string httpClientName,
        string pingPath)
        => new(
            _credentialStore,
            tradingMode,
            label,
            _httpClientFactory,
            httpClientName,
            pingPath,
            _loggerFactory.CreateLogger<BinanceAuthService>());
}

