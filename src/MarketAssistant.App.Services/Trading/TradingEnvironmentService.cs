using MarketAssistant.Services.Settings;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 统一维护当前虚拟币交易环境，避免未保存的设置直接影响运行中的交易链路。
/// </summary>
public sealed class TradingEnvironmentService
{
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<TradingEnvironmentService> _logger;
    private CryptoTradingMode _currentMode;

    public TradingEnvironmentService(
        IUserSettingService userSettingService,
        ILogger<TradingEnvironmentService> logger)
    {
        _userSettingService = userSettingService;
        _logger = logger;
        _currentMode = userSettingService.CurrentSetting.CryptoTradingMode;
    }

    public event Action<CryptoTradingMode>? ModeChanged;

    public CryptoTradingMode CurrentMode => _currentMode;

    public bool IsTestnetMode => _currentMode is CryptoTradingMode.BinanceTestnet
        or CryptoTradingMode.BinanceFuturesTestnet
        or CryptoTradingMode.BinanceSpotDemo;

    public string CurrentModeDisplayName => GetModeDisplayName(_currentMode);

    public string CurrentModeDescription => GetModeDescription(_currentMode);

    public void ApplyMode(CryptoTradingMode mode)
    {
        if (_currentMode == mode)
        {
            return;
        }

        _currentMode = mode;

        // 持久化到用户设置，确保重启后保持一致
        _userSettingService.CurrentSetting.CryptoTradingMode = mode;
        _userSettingService.SaveSettings();

        _logger.LogInformation("虚拟币交易模式已切换为 {Mode} 并已持久化", mode);
        ModeChanged?.Invoke(mode);
    }

    public static string GetModeDisplayName(CryptoTradingMode mode) => mode switch
    {
        CryptoTradingMode.LiveSpot => "Binance 实盘现货",
        CryptoTradingMode.BinanceTestnet => "Binance Spot Testnet",
        CryptoTradingMode.LiveFutures => "Binance 实盘合约",
        CryptoTradingMode.BinanceFuturesTestnet => "Binance Futures Testnet",
        CryptoTradingMode.BinanceSpotDemo => "Binance 现货 Demo",
        _ => "Binance 实盘"
    };

    public static string GetModeDescription(CryptoTradingMode mode) => mode switch
    {
        CryptoTradingMode.LiveSpot => "订单会直接发送到 Binance 现货账户，请确认 API Key 权限与账户余额。",
        CryptoTradingMode.BinanceTestnet => "订单会发送到 Binance Spot Testnet。资产为虚拟资产，远端会周期性重置。",
        CryptoTradingMode.LiveFutures => "订单会直接发送到 Binance U本位合约账户，支持双向持仓与杠杆。请确认 API Key 已开启合约权限。",
        CryptoTradingMode.BinanceFuturesTestnet => "订单会发送到 Binance Futures Testnet (demo-fapi.binance.com)。资产为虚拟资产，用于合约策略验证。",
        CryptoTradingMode.BinanceSpotDemo => "订单会发送到 Binance 现货 Demo 环境 (demo-api.binance.com)。使用实盘账户的虚拟余额，适合策略验证。",
        _ => "订单会直接发送到 Binance 现货账户，请确认 API Key 权限与账户余额。"
    };
}