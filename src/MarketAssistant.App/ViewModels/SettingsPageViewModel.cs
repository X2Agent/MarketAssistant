using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 设置页 ViewModel（核心部分：状态草稿、主题/市场/交易模式选择、保存/重置/导航）。
/// 模型服务商配置见 SettingsPageViewModel.Models.cs；文档向量化与文件选择见 SettingsPageViewModel.Documents.cs。
/// </summary>
public partial class SettingsPageViewModel : ViewModelBase, IDisposable
{
    // RAG 与交易重依赖通过提供者延迟解析：仅在向量化/保存时实例化，
    // 避免首次进入设置页触发整条交易与 RAG 单例链的同步构造
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly IModelDiscoveryService _modelDiscoveryService;
    private readonly Services.Market.MarketContext _marketContext;
    private readonly TradingEnvironmentService _tradingEnvironmentService;
    private readonly IMarketMonitorProvider _marketMonitorProvider;
    private readonly IDialogService _dialogService;
    private readonly DocumentVectorizationService _documentVectorizationService;
    private IStorageProvider? _storageProvider;
    private bool _isInitializingProvider;
    private CancellationTokenSource? _modelFetchCancellationTokenSource;
    private CancellationTokenSource? _vectorizationCts;

    [ObservableProperty]
    private UserSetting _userSetting = new();

    /// <summary>
    /// UserSetting 属性变更时，自动转发关联的计算属性通知
    /// </summary>
    partial void OnUserSettingChanged(UserSetting? oldValue, UserSetting newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= ForwardComputedProperties;

        if (newValue is not null)
            newValue.PropertyChanged += ForwardComputedProperties;
    }

    private void ForwardComputedProperties(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UserSetting.ThemeMode):
                OnPropertyChanged(nameof(IsThemeDefault));
                OnPropertyChanged(nameof(IsThemeLight));
                OnPropertyChanged(nameof(IsThemeDark));
                break;
            case nameof(UserSetting.CurrentMarketType):
                OnPropertyChanged(nameof(IsAShareMarket));
                OnPropertyChanged(nameof(IsCryptoMarket));
                break;
            case nameof(UserSetting.CryptoTradingMode):
                OnPropertyChanged(nameof(IsLiveSpotTradingMode));
                OnPropertyChanged(nameof(IsLiveTradingMode));
                OnPropertyChanged(nameof(IsLiveFuturesTradingMode));
                OnPropertyChanged(nameof(IsFuturesTestnetTradingMode));
                OnPropertyChanged(nameof(IsFuturesTradingMode));
                break;
            case nameof(UserSetting.WebSearchProvider):
                OnPropertyChanged(nameof(IsBingProvider));
                OnPropertyChanged(nameof(IsBraveProvider));
                OnPropertyChanged(nameof(IsTavilyProvider));
                break;
            case nameof(UserSetting.KnowledgeFileDirectory):
                OnPropertyChanged(nameof(IsKnowledgeDirectoryValid));
                break;
        }
    }

    public List<string> WebSearchProviders { get; } = new List<string> { "Bing", "Brave", "Tavily" };

    public List<RiskToleranceLevel> RiskToleranceOptions { get; } = Enum.GetValues<RiskToleranceLevel>().ToList();

    public List<InvestmentHorizonType> InvestmentHorizonOptions { get; } = Enum.GetValues<InvestmentHorizonType>().ToList();

    public List<CryptoTradingMode> CryptoTradingModes { get; } = Enum.GetValues<CryptoTradingMode>().ToList();

    public string ZhiTuApiUrl { get; } = "https://www.zhituapi.com/gettoken.html";
    public string CoinGeckoApiUrl { get; } = "https://www.coingecko.com/en/api";
    public string JinaApiUrl { get; } = "https://jina.ai/embeddings";

    public bool IsThemeDefault
    {
        get => UserSetting.ThemeMode == "Default";
        set
        {
            if (value)
            {
                UserSetting.ThemeMode = "Default";
                ApplyTheme("Default");
            }
        }
    }

    public bool IsThemeLight
    {
        get => UserSetting.ThemeMode == "Light";
        set
        {
            if (value)
            {
                UserSetting.ThemeMode = "Light";
                ApplyTheme("Light");
            }
        }
    }

    public bool IsThemeDark
    {
        get => UserSetting.ThemeMode == "Dark";
        set
        {
            if (value)
            {
                UserSetting.ThemeMode = "Dark";
                ApplyTheme("Dark");
            }
        }
    }

    private static void ApplyTheme(string mode)
    {
        if (Avalonia.Application.Current == null) return;
        Avalonia.Application.Current.RequestedThemeVariant = mode switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
    }

    public bool IsAShareMarket
    {
        get => UserSetting.CurrentMarketType == MarketType.AShare;
        set
        {
            if (value && UserSetting.CurrentMarketType != MarketType.AShare)
            {
                // 仅修改本地草稿 UserSetting，不立即调用 SwitchMarket
                // 避免触发 MainWindowViewModel 重建导航，导致正在编辑的设置丢失
                // 实际市场切换统一在 Save() 中执行
                UserSetting.CurrentMarketType = MarketType.AShare;
                Logger?.LogInformation("市场选择已改为: A股（保存后生效）");
            }
        }
    }

    public bool IsCryptoMarket
    {
        get => UserSetting.CurrentMarketType == MarketType.Crypto;
        set
        {
            if (value && UserSetting.CurrentMarketType != MarketType.Crypto)
            {
                // 仅修改本地 UserSetting，不立即调用 SwitchMarket
                UserSetting.CurrentMarketType = MarketType.Crypto;
                Logger?.LogInformation("市场选择已改为: 虚拟币（保存后生效）");
            }
        }
    }

    public bool IsLiveSpotTradingMode => UserSetting.CryptoTradingMode == CryptoTradingMode.LiveSpot;

    /// <summary>
    /// 是否为实盘模式（现货或合约，共用同一套 API Key）
    /// </summary>
    public bool IsLiveTradingMode => IsLiveSpotTradingMode || IsLiveFuturesTradingMode;

    public bool IsLiveFuturesTradingMode => UserSetting.CryptoTradingMode == CryptoTradingMode.LiveFutures;

    public bool IsFuturesTestnetTradingMode => UserSetting.CryptoTradingMode == CryptoTradingMode.BinanceFuturesTestnet;

    public bool IsFuturesTradingMode => IsLiveFuturesTradingMode || IsFuturesTestnetTradingMode;

    public bool IsBingProvider
    {
        get => UserSetting.WebSearchProvider == "Bing";
        set
        {
            if (value)
                UserSetting.WebSearchProvider = "Bing";
        }
    }

    public bool IsBraveProvider
    {
        get => UserSetting.WebSearchProvider == "Brave";
        set
        {
            if (value)
                UserSetting.WebSearchProvider = "Brave";
        }
    }

    public bool IsTavilyProvider
    {
        get => UserSetting.WebSearchProvider == "Tavily";
        set
        {
            if (value)
                UserSetting.WebSearchProvider = "Tavily";
        }
    }

    public SettingsPageViewModel(
        INotificationService notificationService,
        IUserSettingService userSettingService,
        IModelDiscoveryService modelDiscoveryService,
        Services.Market.MarketContext marketContext,
        TradingEnvironmentService tradingEnvironmentService,
        IMarketMonitorProvider marketMonitorProvider,
        IDialogService dialogService,
        DocumentVectorizationService documentVectorizationService,
        ILogger<SettingsPageViewModel> logger) : base(logger)
    {
        _notificationService = notificationService;
        _userSettingService = userSettingService;
        _modelDiscoveryService = modelDiscoveryService;
        _marketContext = marketContext;
        _tradingEnvironmentService = tradingEnvironmentService;
        _marketMonitorProvider = marketMonitorProvider;
        _dialogService = dialogService;
        _documentVectorizationService = documentVectorizationService;
        _ = SafeExecuteAsync(InitializeAsync, "初始化设置页");
    }

    /// <summary>
    /// 设置 StorageProvider（从 View 调用）
    /// </summary>
    public void SetStorageProvider(IStorageProvider? storageProvider)
    {
        _storageProvider = storageProvider;
    }

    private async Task InitializeAsync()
    {
        // 加载用户设置为独立草稿副本（OnUserSettingChanged 会自动订阅 PropertyChanged）。
        // 不能直接绑定 CurrentSetting 本体：页面上的每次编辑都会立刻生效于全进程，
        // 任何一次切市场的整体落盘都会把未保存的修改（含交易模式改实盘、半填密钥）静默写盘
        UserSetting = _userSettingService.CurrentSetting.Clone();

        // 同步服务商选择。初始化期间保留已保存的 ModelId 和 Endpoint，
        // 仅用户主动切换服务商时才清空这些字段。
        _isInitializingProvider = true;
        try
        {
            SelectedProvider = ModelProviderCatalog.GetProvider(UserSetting.ProviderId)
                ?? ModelProviderCatalog.Providers.First();
        }
        finally
        {
            _isInitializingProvider = false;
        }

        // 模型列表鉴权与具体模型调用鉴权彼此独立。
        var currentKey = UserSetting.ProviderApiKeys.TryGetValue(UserSetting.ProviderId, out var key) ? key : "";
        if (SelectedProvider?.CanListModels(currentKey) == true)
        {
            await FetchModels();
        }

        // 同步市场类型到MarketContext
        _marketContext.SwitchMarket(UserSetting.CurrentMarketType);
        LoadAnalystRoles();
        ApplyTheme(UserSetting.ThemeMode);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            foreach (var role in AnalystRoles)
            {
                UserSetting.EnabledAnalystRoles[role.Id] = role.IsEnabled;
            }

            // 监控运行中切换到实盘会使后续订单进入真实账户，必须二次确认。
            var targetMode = UserSetting.CryptoTradingMode;
            if (TradingEnvironmentService.RequiresLiveModeConfirmation(
                    _tradingEnvironmentService.CurrentMode,
                    targetMode,
                    _marketMonitorProvider.GetMonitor().IsRunning))
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "切换到实盘模式",
                    "市场监控正在运行。保存后会先停止监控并切换到实盘模式，后续触发的交易将发送到真实账户。\n\n请确认是否继续？",
                    "确认切换",
                    "取消");

                if (!confirmed)
                {
                    _notificationService.ShowInfo("已取消保存设置");
                    return;
                }
            }

            // 同步市场类型到MarketContext
            _marketContext.SwitchMarket(UserSetting.CurrentMarketType);

            // 提交草稿副本（而非页面持有的编辑中实例），保证服务内部状态与页面编辑解耦
            _userSettingService.UpdateSettings(UserSetting.Clone());
            await _tradingEnvironmentService.ApplyModeAsync(UserSetting.CryptoTradingMode);
            _notificationService.ShowSuccess("设置已保存");
            Logger?.LogInformation("保存设置，市场类型：{MarketType}，交易模式：{TradingMode}",
                UserSetting.CurrentMarketType,
                UserSetting.CryptoTradingMode);
        }, "保存设置");
    }

    [RelayCommand]
    private async Task Reset()
    {
        await SafeExecuteAsync(async () =>
        {
            _userSettingService.ResetSettings();
            UserSetting = _userSettingService.CurrentSetting.Clone();

            _isInitializingProvider = true;
            try
            {
                SelectedProvider = ModelProviderCatalog.GetProvider(UserSetting.ProviderId)
                    ?? ModelProviderCatalog.Providers.First();
            }
            finally
            {
                _isInitializingProvider = false;
            }

            _marketContext.SwitchMarket(UserSetting.CurrentMarketType);
            ApplyTheme(UserSetting.ThemeMode);
            await _tradingEnvironmentService.ApplyModeAsync(UserSetting.CryptoTradingMode);
            LoadAnalystRoles();
            _notificationService.ShowSuccess("设置已重置为默认值");
            Logger?.LogInformation("重置设置为默认值");
        }, "重置设置");
    }

    [RelayCommand]
    private void NavigateToMCPConfig()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage("MCPConfig"));
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _modelFetchCancellationTokenSource?.Cancel();
        _modelFetchCancellationTokenSource = null;

        // 只取消不 Dispose：在飞请求仍持有该令牌，其自身 finally 负责释放 CTS
        _vectorizationCts?.Cancel();
        _vectorizationCts = null;

        // 取消 UserSetting.PropertyChanged 订阅，避免 Singleton 持有已释放 ViewModel 的引用
        if (UserSetting is not null)
            UserSetting.PropertyChanged -= ForwardComputedProperties;

        GC.SuppressFinalize(this);
    }
}
