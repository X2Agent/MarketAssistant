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
        var (apiKey, secretKey) = _credentialStore.GetCredentials(mode);
        ApiKey = apiKey;
        SecretKey = secretKey;
        ModeDescription = TradingEnvironmentService.GetModeDescription(mode);
    }

    private void RefreshCredentialStatuses()
    {
        CredentialStatuses.Clear();
        foreach (var mode in TradingModes)
        {
            CredentialStatuses.Add(new CredentialStatus
            {
                Mode = mode,
                DisplayName = TradingEnvironmentService.GetModeDisplayName(mode),
                IsConfigured = _credentialStore.IsConfigured(mode)
            });
        }
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
