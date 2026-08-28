using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 统一维护当前虚拟币交易环境，避免未保存的设置直接影响运行中的交易链路。
/// 切换模式前会先停止正在运行的 <see cref="MarketMonitor"/>，防止在途订单、价格订阅
/// 与账户数据在切换瞬间被路由到新环境造成状态错乱。
/// </summary>
public sealed class TradingEnvironmentService
{
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<TradingEnvironmentService> _logger;
    private readonly Func<MarketMonitor> _marketMonitorFactory;

    // 后台下单链路（MarketMonitor 消费者/TradeExecutor）与 UI 线程并发读写，
    // 必须 volatile 保证模式切换立即对后台线程可见，避免"切到实盘仍在 Demo 下单"的反向错误
    private volatile CryptoTradingMode _currentMode;

    public TradingEnvironmentService(
        IUserSettingService userSettingService,
        Func<MarketMonitor> marketMonitorFactory,
        ILogger<TradingEnvironmentService> logger)
    {
        _userSettingService = userSettingService;
        _marketMonitorFactory = marketMonitorFactory;
        _logger = logger;
        _currentMode = userSettingService.CurrentSetting.CryptoTradingMode;
    }

    public event Action<CryptoTradingMode>? ModeChanged;

    public CryptoTradingMode CurrentMode => _currentMode;

    public bool IsTestnetMode => _currentMode is CryptoTradingMode.BinanceFuturesTestnet
        or CryptoTradingMode.BinanceSpotDemo;

    public string CurrentModeDisplayName => GetModeDisplayName(_currentMode);

    public string CurrentModeDescription => GetModeDescription(_currentMode);

    /// <summary>
    /// 判断运行中的市场监控切换到实盘环境时是否必须二次确认。
    /// </summary>
    public static bool RequiresLiveModeConfirmation(
        CryptoTradingMode currentMode,
        CryptoTradingMode targetMode,
        bool isMonitorRunning)
    {
        return isMonitorRunning &&
               targetMode != currentMode &&
               targetMode is CryptoTradingMode.LiveSpot or CryptoTradingMode.LiveFutures;
    }

    /// <summary>
    /// 切换交易模式。若监控正在运行，先等待其完全停止（最长 10 秒）再切换；
    /// 停止超时（存在未完成的在途策略任务）时中止切换并抛错，
    /// 防止在途订单被路由到新环境造成"该下单到模拟盘却落到实盘"的资金安全事故。
    /// </summary>
    public async Task ApplyModeAsync(CryptoTradingMode mode)
    {
        if (_currentMode == mode)
        {
            return;
        }

        var monitor = _marketMonitorFactory();
        if (monitor.IsRunning)
        {
            _logger.LogInformation("切换交易模式前停止市场监控: {OldMode} → {NewMode}", _currentMode, mode);
            var stopped = await monitor.TryStopAsync().ConfigureAwait(false);
            if (!stopped)
            {
                _logger.LogError(
                    "市场监控停止超时（存在在途策略任务），已中止交易模式切换: {OldMode} → {NewMode}",
                    _currentMode, mode);
                throw new FriendlyException(
                    "存在正在执行的策略任务，市场监控未能及时停止，交易模式切换已中止。请稍后重试；若持续失败请检查网络后重启应用。");
            }
        }

        _currentMode = mode;

        // 持久化到用户设置，确保重启后保持一致；
        // 与持久化共用同步边界，避免与其它线程的设置保存交错
        _userSettingService.UpdateSetting(setting => setting.CryptoTradingMode = mode);

        _logger.LogInformation("虚拟币交易模式已切换为 {Mode} 并已持久化", mode);
        ModeChanged?.Invoke(mode);
    }

    public static string GetModeDisplayName(CryptoTradingMode mode) => mode switch
    {
        CryptoTradingMode.LiveSpot => "Binance 实盘现货",
        CryptoTradingMode.LiveFutures => "Binance 实盘合约",
        CryptoTradingMode.BinanceFuturesTestnet => "Binance Futures Testnet",
        CryptoTradingMode.BinanceSpotDemo => "Binance 现货 Demo",
        _ => "Binance 实盘"
    };

    public static string GetModeDescription(CryptoTradingMode mode) => mode switch
    {
        CryptoTradingMode.LiveSpot => "订单会直接发送到 Binance 现货账户，请确认 API Key 权限与账户余额。",
        CryptoTradingMode.LiveFutures => "订单会直接发送到 Binance U本位合约账户，支持双向持仓与杠杆。请确认 API Key 已开启合约权限。",
        CryptoTradingMode.BinanceFuturesTestnet => "订单会发送到 Binance Futures Testnet (demo-fapi.binance.com)。资产为虚拟资产，用于合约策略验证。",
        CryptoTradingMode.BinanceSpotDemo => "订单会发送到 Binance 现货 Demo 环境 (demo-api.binance.com)。使用实盘账户的虚拟余额，适合策略验证。",
        _ => "订单会直接发送到 Binance 现货账户，请确认 API Key 权限与账户余额。"
    };
}