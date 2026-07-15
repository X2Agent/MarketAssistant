using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

/// <summary>
/// 交易 API 密钥配置 ViewModel，嵌入交易页面。
/// 按交易模式隔离密钥，密钥通过 ITradingCredentialStore 加密存储。
/// </summary>
public partial class ApiKeyConfigViewModel : ViewModelBase
{
    private readonly ITradingCredentialStore _credentialStore;
    private readonly TradingEnvironmentService _tradingEnvironmentService;
    private readonly INotificationService _notificationService;
    private readonly MarketMonitor _marketMonitor;
    private readonly IDialogService _dialogService;

    public override string Title => "API 密钥";

    public List<CryptoTradingMode> TradingModes { get; } = Enum.GetValues<CryptoTradingMode>().ToList();

    [ObservableProperty]
    private CryptoTradingMode _selectedMode;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _secretKey = string.Empty;

    [ObservableProperty]
    private string _modeDescription = string.Empty;

    /// <summary>
    /// 各交易模式的密钥配置状态（用于显示已配置/未配置标记）
    /// </summary>
    public ObservableCollection<CredentialStatus> CredentialStatuses { get; } = new();

    /// <summary>
    /// 当前选中模式是否为合约模式
    /// </summary>
    public bool IsFuturesMode => SelectedMode is CryptoTradingMode.LiveFutures or CryptoTradingMode.BinanceFuturesTestnet;

    /// <summary>
    /// 当前选中模式是否为实盘模式（真实资金）
    /// </summary>
    public bool IsLiveMode => SelectedMode is CryptoTradingMode.LiveSpot or CryptoTradingMode.LiveFutures;

    /// <summary>
    /// 当前选中模式是否为 Demo 模式（虚拟资金）。现货 Demo 与 Futures Testnet 共用同一组 Demo API Key。
    /// </summary>
    public bool IsDemoMode => SelectedMode is CryptoTradingMode.BinanceFuturesTestnet or CryptoTradingMode.BinanceSpotDemo;

    public ApiKeyConfigViewModel(
        ITradingCredentialStore credentialStore,
        TradingEnvironmentService tradingEnvironmentService,
        INotificationService notificationService,
        MarketMonitor marketMonitor,
        IDialogService dialogService,
        ILogger<ApiKeyConfigViewModel> logger)
        : base(logger)
    {
        _credentialStore = credentialStore;
        _tradingEnvironmentService = tradingEnvironmentService;
        _notificationService = notificationService;
        _marketMonitor = marketMonitor;
        _dialogService = dialogService;
        _selectedMode = _tradingEnvironmentService.CurrentMode;
        RefreshCredentialStatuses();
        LoadCredentialsForMode(_selectedMode);
    }

    partial void OnSelectedModeChanged(CryptoTradingMode value)
    {
        LoadCredentialsForMode(value);
        ModeDescription = TradingEnvironmentService.GetModeDescription(value);
        OnPropertyChanged(nameof(IsFuturesMode));
        OnPropertyChanged(nameof(IsLiveMode));
        OnPropertyChanged(nameof(IsDemoMode));
    }

    [RelayCommand]
    private void Save()
    {
        SafeExecute(() =>
        {
            if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(SecretKey))
            {
                _notificationService.ShowWarning("API Key 和 Secret Key 不能为空");
                return;
            }

            _credentialStore.SetCredentials(SelectedMode, ApiKey.Trim(), SecretKey.Trim());

            // 实盘现货/合约共用同一套 binance.com 账户密钥，联动写入避免重复配置
            if (IsLiveMode)
            {
                var pairedMode = SelectedMode == CryptoTradingMode.LiveSpot
                    ? CryptoTradingMode.LiveFutures
                    : CryptoTradingMode.LiveSpot;
                _credentialStore.SetCredentials(pairedMode, ApiKey.Trim(), SecretKey.Trim());
            }

            // Demo 现货/合约共用同一组 Demo API Key（实测 demo-api / demo-fapi 端点共享密钥）
            if (IsDemoMode)
            {
                var pairedDemoMode = SelectedMode == CryptoTradingMode.BinanceSpotDemo
                    ? CryptoTradingMode.BinanceFuturesTestnet
                    : CryptoTradingMode.BinanceSpotDemo;
                _credentialStore.SetCredentials(pairedDemoMode, ApiKey.Trim(), SecretKey.Trim());
            }

            RefreshCredentialStatuses();
            _notificationService.ShowSuccess($"{TradingEnvironmentService.GetModeDisplayName(SelectedMode)} 密钥已加密保存");
            Logger?.LogInformation("交易密钥已保存：{Mode}", SelectedMode);
        }, "保存密钥");
    }

    [RelayCommand]
    private void Clear()
    {
        SafeExecute(() =>
        {
            _credentialStore.ClearCredentials(SelectedMode);

            // 实盘现货/合约共享密钥，清除时联动清除配对模式
            if (IsLiveMode)
            {
                var pairedMode = SelectedMode == CryptoTradingMode.LiveSpot
                    ? CryptoTradingMode.LiveFutures
                    : CryptoTradingMode.LiveSpot;
                _credentialStore.ClearCredentials(pairedMode);
            }

            // Demo 现货/合约共享密钥，清除时联动清除配对模式
            if (IsDemoMode)
            {
                var pairedDemoMode = SelectedMode == CryptoTradingMode.BinanceSpotDemo
                    ? CryptoTradingMode.BinanceFuturesTestnet
                    : CryptoTradingMode.BinanceSpotDemo;
                _credentialStore.ClearCredentials(pairedDemoMode);
            }

            ApiKey = string.Empty;
            SecretKey = string.Empty;
            RefreshCredentialStatuses();
            _notificationService.ShowSuccess("密钥已清除");
            Logger?.LogInformation("交易密钥已清除：{Mode}", SelectedMode);
        }, "清除密钥");
    }

    [RelayCommand]
    private async Task ApplyModeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            // 切换到实盘模式时若监控正在运行，弹窗警告（运行中切换会立即对实盘账户下单）
            if (IsLiveMode && _marketMonitor.IsRunning)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "切换到实盘模式",
                    "⚠️ 市场监控正在运行中！切换到实盘模式后，后续触发的交易将立即对真实账户下单。\n\n请确认是否继续切换到实盘模式？",
                    "确认切换",
                    "取消");

                if (!confirmed)
                {
                    _notificationService.ShowInfo("已取消切换交易模式");
                    return;
                }
            }

            _tradingEnvironmentService.ApplyMode(SelectedMode);
            _notificationService.ShowSuccess($"已切换到 {TradingEnvironmentService.GetModeDisplayName(SelectedMode)}");
        }, "切换交易模式");
    }

    private void LoadCredentialsForMode(CryptoTradingMode mode)
    {
        // 实盘现货/合约共享密钥，统一直接从 LiveSpot 加载，保证两侧显示一致
        // Demo 现货/合约共享密钥，统一直接从 BinanceSpotDemo 加载
        var loadMode = mode switch
        {
            CryptoTradingMode.LiveSpot or CryptoTradingMode.LiveFutures => CryptoTradingMode.LiveSpot,
            CryptoTradingMode.BinanceSpotDemo or CryptoTradingMode.BinanceFuturesTestnet => CryptoTradingMode.BinanceSpotDemo,
            _ => mode
        };
        var (apiKey, secretKey) = _credentialStore.GetCredentials(loadMode);
        ApiKey = apiKey;
        SecretKey = secretKey;
        ModeDescription = TradingEnvironmentService.GetModeDescription(mode);
    }

    private void RefreshCredentialStatuses()
    {
        CredentialStatuses.Clear();

        // 实盘现货/合约共享密钥，合并为一行显示
        var liveConfigured = _credentialStore.IsConfigured(CryptoTradingMode.LiveSpot)
                             && _credentialStore.IsConfigured(CryptoTradingMode.LiveFutures);
        CredentialStatuses.Add(new CredentialStatus
        {
            Mode = CryptoTradingMode.LiveSpot,
            DisplayName = "实盘（现货 + 合约共享）",
            IsConfigured = liveConfigured
        });

        // Demo 现货/合约共享同一组 Demo API Key，合并为一行显示
        var demoConfigured = _credentialStore.IsConfigured(CryptoTradingMode.BinanceSpotDemo)
                             && _credentialStore.IsConfigured(CryptoTradingMode.BinanceFuturesTestnet);
        CredentialStatuses.Add(new CredentialStatus
        {
            Mode = CryptoTradingMode.BinanceSpotDemo,
            DisplayName = "Demo（现货 Demo + Futures Testnet 共享）",
            IsConfigured = demoConfigured
        });
    }
}

/// <summary>
/// 单个交易模式的密钥配置状态
/// </summary>
public class CredentialStatus
{
    public CryptoTradingMode Mode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public string StatusText => IsConfigured ? "已配置" : "未配置";
}
