using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Agents.Analysts;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace MarketAssistant.ViewModels;

public partial class SettingsPageViewModel : ViewModelBase, IDisposable
{
    // RAG 与交易重依赖通过提供者延迟解析：仅在向量化/保存时实例化，
    // 避免首次进入设置页触发整条交易与 RAG 单例链的同步构造
    private readonly IRagInfrastructureProvider _ragInfrastructureProvider;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly IModelDiscoveryService _modelDiscoveryService;
    private readonly Services.Market.MarketContext _marketContext;
    private readonly TradingEnvironmentService _tradingEnvironmentService;
    private readonly IMarketMonitorProvider _marketMonitorProvider;
    private readonly IDialogService _dialogService;
    private IStorageProvider? _storageProvider;
    private bool _isInitializingProvider;
    private CancellationTokenSource? _modelFetchCancellationTokenSource;
    private CancellationTokenSource? _vectorizationCts;

    // 向量化在途守卫必须跨 ViewModel 实例生效：离开设置页会释放当前 VM 并新建实例，
    // 实例级标志挡不住"旧循环仍在跑 + 新页面再次启动"的并发向量化
    private static int _activeVectorizations;

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

    [ObservableProperty]
    private ObservableCollection<string> _models = [];

    // 服务商列表（目录运行期不变，缓存实例避免 ComboBox 每次绑定求值新建 List）
    public List<ModelProvider> Providers { get; } = ModelProviderCatalog.Providers.ToList();

    [ObservableProperty]
    private ModelProvider? _selectedProvider;

    // 当前服务商的 API Key（从 ProviderApiKeys 中读取当前服务商的 Key）
    public string ApiKey
    {
        get => UserSetting.ProviderApiKeys.TryGetValue(UserSetting.ProviderId, out var key) ? key : "";
        set
        {
            if (string.IsNullOrWhiteSpace(UserSetting.ProviderId) || ApiKey == value)
                return;

            UserSetting.ProviderApiKeys[UserSetting.ProviderId] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanFetchModels));
            OnPropertyChanged(nameof(ModelDiscoveryHint));
            FetchModelsCommand.NotifyCanExecuteChanged();
        }
    }

    // 是否显示 API Key 配置。OpenCode Zen 即使当前使用免费模型也允许配置 Key。
    public bool SupportsApiKeyConfiguration => SelectedProvider?.RequiresApiKey ?? false;

    // 当前模型是否强制要求 API Key，用于提示而不是控制输入框可见性。
    public bool IsApiKeyRequiredForSelectedModel =>
        SelectedProvider?.RequiresApiKeyForModel(ModelId) ?? false;

    public string ApiKeyHint => IsApiKeyRequiredForSelectedModel
        ? "当前模型需要 API Key"
        : "API Key 可选；留空使用免费模型，配置后可访问账号授权模型";

    public string? ProviderApiKeyUrl => SelectedProvider?.ApiKeyUrl;

    public bool CanOverrideEndpoint => SelectedProvider?.AllowsEndpointOverride ?? false;

    // 当前服务商的活动模型 ID（按服务商存储于 ProviderModelIds）。
    public string ModelId
    {
        get => UserSetting.ProviderModelIds.GetValueOrDefault(UserSetting.ProviderId, string.Empty);
        set
        {
            if (string.IsNullOrWhiteSpace(UserSetting.ProviderId))
                return;
            if (ModelId == value)
                return;

            UserSetting.ProviderModelIds[UserSetting.ProviderId] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsApiKeyRequiredForSelectedModel));
            OnPropertyChanged(nameof(ApiKeyHint));
        }
    }

    // 仅本地部署和自定义服务允许覆盖默认端点。按服务商存储于 ProviderEndpoints。
    public string Endpoint
    {
        get => UserSetting.ProviderEndpoints.GetValueOrDefault(UserSetting.ProviderId, string.Empty);
        set
        {
            if (string.IsNullOrWhiteSpace(UserSetting.ProviderId))
                return;
            if (Endpoint == value)
                return;

            UserSetting.ProviderEndpoints[UserSetting.ProviderId] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveEndpoint));
        }
    }

    public string EndpointPlaceholder => string.IsNullOrWhiteSpace(SelectedProvider?.DefaultEndpoint)
        ? "请输入完整的 API Base URL"
        : $"默认：{SelectedProvider.DefaultEndpoint}";

    public string EffectiveEndpoint => string.IsNullOrWhiteSpace(Endpoint)
        ? SelectedProvider?.DefaultEndpoint ?? string.Empty
        : Endpoint.Trim();

    public bool SupportsModelListing => SelectedProvider?.SupportsModelListing ?? false;

    public bool CanFetchModels =>
        SelectedProvider is { SupportsModelListing: true } provider &&
        !IsLoadingModels &&
        (!provider.ModelListingRequiresApiKey || !string.IsNullOrWhiteSpace(ApiKey));

    public string ModelDiscoveryHint
    {
        get
        {
            if (SelectedProvider is not { } provider)
                return "请先选择模型服务商";

            if (!provider.SupportsModelListing)
                return "该服务商不提供模型目录，请直接输入模型 ID";

            if (provider.ModelListingRequiresApiKey && string.IsNullOrWhiteSpace(ApiKey))
                return "配置 API Key 后可获取模型目录，也可以直接输入模型 ID";

            return string.IsNullOrWhiteSpace(ModelDiscoveryStatus)
                ? "可从服务商获取模型目录，也可以直接输入未列出的模型 ID"
                : ModelDiscoveryStatus;
        }
    }

    [ObservableProperty]
    private string _modelDiscoveryStatus = string.Empty;

    partial void OnModelDiscoveryStatusChanged(string value) =>
        OnPropertyChanged(nameof(ModelDiscoveryHint));

    [ObservableProperty]
    private bool _isLoadingModels;

    partial void OnIsLoadingModelsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanFetchModels));
        OnPropertyChanged(nameof(ModelDiscoveryHint));
        FetchModelsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProviderChanged(ModelProvider? oldValue, ModelProvider? newValue)
    {
        if (newValue is null)
            return;

        UserSetting.ProviderId = newValue.Id;

        _modelFetchCancellationTokenSource?.Cancel();
        Models.Clear();
        ModelDiscoveryStatus = string.Empty;

        // ModelId/Endpoint 按服务商存储于字典，切换后通知绑定重新读取即可。
        OnPropertyChanged(nameof(ApiKey));
        OnPropertyChanged(nameof(ModelId));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(SupportsApiKeyConfiguration));
        OnPropertyChanged(nameof(IsApiKeyRequiredForSelectedModel));
        OnPropertyChanged(nameof(ApiKeyHint));
        OnPropertyChanged(nameof(ProviderApiKeyUrl));
        OnPropertyChanged(nameof(CanOverrideEndpoint));
        OnPropertyChanged(nameof(EndpointPlaceholder));
        OnPropertyChanged(nameof(EffectiveEndpoint));
        OnPropertyChanged(nameof(SupportsModelListing));
        OnPropertyChanged(nameof(CanFetchModels));
        OnPropertyChanged(nameof(ModelDiscoveryHint));
        FetchModelsCommand.NotifyCanExecuteChanged();

        if (!_isInitializingProvider)
        {
            // 模型列表鉴权与具体模型调用鉴权彼此独立。
            var currentKey = UserSetting.ProviderApiKeys.TryGetValue(newValue.Id, out var key) ? key : "";
            if (newValue.CanListModels(currentKey))
            {
                _ = FetchModels();
            }
        }
    }

    [ObservableProperty]
    private ObservableCollection<AnalystRoleViewModel> _analystRoles = new();

    public bool IsKnowledgeDirectoryValid => !string.IsNullOrEmpty(UserSetting.KnowledgeFileDirectory) && Directory.Exists(UserSetting.KnowledgeFileDirectory);

    [ObservableProperty]
    private bool _isVectorizing;

    // 向量化进度（0-100）
    [ObservableProperty]
    private int _vectorizingProgress;

    [ObservableProperty]
    private string _vectorizingProgressText = "";

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
        IRagInfrastructureProvider ragInfrastructureProvider,
        INotificationService notificationService,
        IUserSettingService userSettingService,
        IModelDiscoveryService modelDiscoveryService,
        Services.Market.MarketContext marketContext,
        TradingEnvironmentService tradingEnvironmentService,
        IMarketMonitorProvider marketMonitorProvider,
        IDialogService dialogService,
        ILogger<SettingsPageViewModel> logger) : base(logger)
    {
        _ragInfrastructureProvider = ragInfrastructureProvider;
        _notificationService = notificationService;
        _userSettingService = userSettingService;
        _modelDiscoveryService = modelDiscoveryService;
        _marketContext = marketContext;
        _tradingEnvironmentService = tradingEnvironmentService;
        _marketMonitorProvider = marketMonitorProvider;
        _dialogService = dialogService;
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

    private void LoadAnalystRoles()
    {
        AnalystRoles.Clear();
        var agentTypes = AnalystTypeRegistry.GetConcreteAnalystTypes();

        foreach (var type in agentTypes)
        {
            var displayName = type.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? type.Name;
            var description = type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            var isRequired = type.GetCustomAttribute<RequiredAnalystAttribute>() != null;

            // 按当前市场过滤角色列表（未标注 SupportedMarkets 视为全市场支持）
            if (!SupportedMarketsAttribute.SupportsMarket(type, _marketContext.CurrentMarket)) continue;

            var id = type.Name;

            var isEnabled = false;
            if (UserSetting.EnabledAnalystRoles.TryGetValue(id, out var enabled))
            {
                isEnabled = enabled;
            }

            if (isRequired) isEnabled = true;

            AnalystRoles.Add(new AnalystRoleViewModel
            {
                Id = id,
                Name = displayName,
                Description = description,
                IsRequired = isRequired,
                IsEnabled = isEnabled
            });
        }
    }

    /// <summary>
    /// 打开API密钥网站命令
    /// </summary>
    [RelayCommand]
    private Task OpenModelApiWebsite() => ProviderApiKeyUrl != null ? OpenUrlAsync(ProviderApiKeyUrl) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenZhiTuApiWebsite() => OpenUrlAsync(ZhiTuApiUrl);

    [RelayCommand]
    private Task OpenCoinGeckoApiWebsite() => OpenUrlAsync(CoinGeckoApiUrl);

    [RelayCommand]
    private Task OpenEmbeddingApiWebsite() => OpenUrlAsync(JinaApiUrl);

    /// <summary>
    /// 选择知识库目录
    /// </summary>
    [RelayCommand]
    private async Task SelectKnowledgeDirectory()
    {
        if (_storageProvider == null) return;

        await SafeExecuteAsync(async () =>
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择知识库目录",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                UserSetting.KnowledgeFileDirectory = folders[0].Path.LocalPath;
            }
        }, "选择知识库目录");
    }

    /// <summary>
    /// 选择日志路径
    /// </summary>
    [RelayCommand]
    private async Task SelectLogPath()
    {
        if (_storageProvider == null) return;

        await SafeExecuteAsync(async () =>
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择日志路径",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                UserSetting.LogPath = Path.Combine(folders[0].Path.LocalPath, "logs");
            }
        }, "选择日志路径");
    }

    [RelayCommand]
    private async Task VectorizeDocuments()
    {
        if (!IsKnowledgeDirectoryValid)
        {
            _notificationService.ShowWarning("知识库目录无效，请先选择有效的目录");
            Logger?.LogWarning("知识库目录无效，无法进行向量化");
            return;
        }

        if (System.Threading.Interlocked.CompareExchange(ref _activeVectorizations, 1, 0) != 0)
        {
            _notificationService.ShowWarning("已有一个向量化任务在后台进行中，请等待其完成后再试");
            Logger?.LogWarning("拒绝并发的向量化请求");
            return;
        }

        var cts = new CancellationTokenSource();
        _vectorizationCts = cts;
        try
        {
            IsVectorizing = true;
            VectorizingProgress = 0;
            VectorizingProgressText = "准备中...";

            Logger?.LogInformation("开始向量化知识库目录: {Directory}", UserSetting.KnowledgeFileDirectory);

            // 创建嵌入生成器（只在实际需要时创建）
            var embeddingGenerator = _ragInfrastructureProvider.GetEmbeddingFactory().Create();

            var collectionName = UserSetting.VectorCollectionName;
            var collection = _ragInfrastructureProvider.GetVectorStore().GetCollection<string, TextParagraph>(collectionName);
            await collection.EnsureCollectionExistsAsync();
            Logger?.LogInformation("使用向量集合: {CollectionName}", collectionName);

            var supportedExtensions = new[] { ".pdf", ".docx", ".md" };

            var files = Directory.GetFiles(UserSetting.KnowledgeFileDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (files.Count == 0)
            {
                _notificationService.ShowWarning($"未找到支持的文档（支持：{string.Join(", ", supportedExtensions)}）");
                Logger?.LogWarning("知识库目录中没有找到支持的文档");
                return;
            }

            var totalFiles = files.Count;
            Logger?.LogInformation("找到 {Count} 个文档需要向量化", totalFiles);
            _notificationService.ShowInfo($"开始向量化 {totalFiles} 个文档...");

            var ragIngestionService = _ragInfrastructureProvider.GetIngestionService();
            var successCount = 0;
            var partialCount = 0;
            var failedCount = 0;
            var failedFiles = new List<string>();
            var partialFiles = new List<string>();

            for (int i = 0; i < totalFiles; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var file = files[i];
                var fileName = Path.GetFileName(file);
                var fileExtension = Path.GetExtension(file).ToUpperInvariant();

                try
                {
                    var currentIndex = i + 1;
                    VectorizingProgress = (int)((double)currentIndex / totalFiles * 100);
                    VectorizingProgressText = $"正在处理 {currentIndex}/{totalFiles}: {fileName}";

                    Logger?.LogInformation("正在处理 ({Index}/{Total}): {FileName} [{Extension}]",
                        currentIndex, totalFiles, fileName, fileExtension);

                    // 执行向量化：根据结构化结果区分完全成功/部分成功/失败
                    var result = await ragIngestionService.IngestFileAsync(
                        collection, collectionName, file, embeddingGenerator, cts.Token);

                    if (result.IsSuccess)
                    {
                        successCount++;
                        Logger?.LogInformation("✓ 成功向量化: {FileName}", fileName);
                    }
                    else if (result.IsPartialSuccess)
                    {
                        // 部分成功不计入完全成功
                        partialCount++;
                        partialFiles.Add($"{fileName}（{result.Failures.Count} 个块失败）");
                        Logger?.LogWarning("△ 部分成功向量化: {FileName}，{BlockCount} 块中 {Failed} 个失败",
                            fileName, result.BlockCount, result.Failures.Count);
                    }
                    else
                    {
                        failedCount++;
                        failedFiles.Add(fileName);
                        var reason = result.Failures.FirstOrDefault()?.Message ?? "没有内容入库";
                        Logger?.LogError("✗ 向量化失败: {FileName} - {Reason}", fileName, reason);
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    VectorizingProgressText = "向量化已取消";
                    _notificationService.ShowWarning("向量化已取消。已完成的部分保持有效。");
                    Logger?.LogWarning("向量化被用户取消");
                    return;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    failedFiles.Add(fileName);
                    Logger?.LogError(ex, "✗ 向量化失败: {FileName} - {ErrorMessage}", fileName, ex.Message);

                    // 单个文件失败不中断整体流程，继续处理下一个
                }
            }

            // 显示完成消息（三态：完全成功 / 部分成功 / 失败）
            VectorizingProgress = 100;
            if (failedCount == 0 && partialCount == 0)
            {
                VectorizingProgressText = $"✅ 全部完成！共 {successCount} 个文件";
                _notificationService.ShowSuccess($"✅ 所有文档向量化完成！\n成功处理 {successCount} 个文件");
                Logger?.LogInformation("向量化完成：成功 {Success}/{Total} 个", successCount, totalFiles);
            }
            else
            {
                var summaryText = $"⚠️ 完成（存在失败）: {successCount} 成功, {partialCount} 部分成功, {failedCount} 失败";
                VectorizingProgressText = summaryText;

                var failedList = string.Join("\n- ", failedFiles.Take(5));
                if (failedFiles.Count > 5)
                {
                    failedList += $"\n... 还有 {failedFiles.Count - 5} 个";
                }

                var partialList = string.Join("\n- ", partialFiles.Take(5));
                if (partialFiles.Count > 5)
                {
                    partialList += $"\n... 还有 {partialFiles.Count - 5} 个";
                }

                _notificationService.ShowWarning(
                    $"向量化完成：\n✓ 完全成功 {successCount} 个\n△ 部分成功 {partialCount} 个\n✗ 失败 {failedCount} 个" +
                    (partialFiles.Count > 0 ? $"\n\n部分成功（存在失败块）：\n- {partialList}" : string.Empty) +
                    (failedFiles.Count > 0 ? $"\n\n失败文件：\n- {failedList}" : string.Empty));

                Logger?.LogWarning("向量化完成：成功 {Success} 个，部分成功 {Partial} 个，失败 {Failed} 个，总计 {Total} 个",
                    successCount, partialCount, failedCount, totalFiles);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 取消发生在文件间隙或准备阶段
            VectorizingProgressText = "向量化已取消";
            _notificationService.ShowWarning("向量化已取消。已完成的部分保持有效。");
            Logger?.LogWarning("向量化被用户取消");
        }
        catch (Exception ex)
        {
            VectorizingProgressText = "向量化失败";
            Logger?.LogError(ex, "向量化过程发生严重错误");
            _notificationService.ShowError(ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "向量化"));
        }
        finally
        {
            IsVectorizing = false;
            if (ReferenceEquals(_vectorizationCts, cts))
                _vectorizationCts = null;
            cts.Dispose();
            System.Threading.Interlocked.Exchange(ref _activeVectorizations, 0);
        }
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

    /// <summary>
    /// 从服务商 API 获取模型列表（用户填好 API Key 后手动触发）
    /// </summary>
    private bool CanFetchModelsCommand() => CanFetchModels;

    [RelayCommand(CanExecute = nameof(CanFetchModelsCommand))]
    private async Task FetchModels()
    {
        var provider = SelectedProvider;
        if (provider is null || !provider.SupportsModelListing)
            return;

        // 只取消上一个在飞请求，不 Dispose（其令牌仍被在飞请求持有，由该请求自身 finally 释放）
        _modelFetchCancellationTokenSource?.Cancel();
        var cts = new CancellationTokenSource();
        _modelFetchCancellationTokenSource = cts;
        var requestedProviderId = provider.Id;

        ModelDiscoveryStatus = $"正在从 {provider.DisplayName} 获取模型目录...";
        IsLoadingModels = true;
        try
        {
            var apiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey;
            var endpoint = provider.AllowsEndpointOverride && !string.IsNullOrWhiteSpace(Endpoint)
                ? Endpoint
                : null;
            var models = await _modelDiscoveryService.ListModelsAsync(
                provider,
                apiKey,
                endpoint,
                cts.Token);

            // 取消不能保证远端立即停止；响应落 UI 前再次校验 Provider 身份。
            if (cts.IsCancellationRequested || SelectedProvider?.Id != requestedProviderId)
                return;

            Models.Clear();
            foreach (var model in models)
                Models.Add(model);

            ModelDiscoveryStatus = Models.Count == 0
                ? "服务商未返回可用模型，请直接输入模型 ID"
                : $"已获取 {Models.Count} 个模型，可直接选择或继续手工输入";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Logger?.LogDebug("已取消服务商 {ProviderId} 的模型列表请求", requestedProviderId);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            HandleModelDiscoveryFailure(
                requestedProviderId,
                ex,
                $"{provider.DisplayName} 拒绝访问，请检查 API Key");
        }
        catch (Exception ex)
        {
            HandleModelDiscoveryFailure(
                requestedProviderId,
                ex,
                $"获取失败：{ErrorMessageMapper.GetUserFriendlyMessage(ex)}");
        }
        finally
        {
            if (ReferenceEquals(_modelFetchCancellationTokenSource, cts))
            {
                _modelFetchCancellationTokenSource = null;
                IsLoadingModels = false;
            }
            cts.Dispose();
        }
    }

    private void HandleModelDiscoveryFailure(
        string requestedProviderId,
        Exception exception,
        string status)
    {
        if (SelectedProvider?.Id != requestedProviderId)
            return;

        ModelDiscoveryStatus = $"{status}，仍可直接输入模型 ID";
        Logger?.LogWarning(exception, "获取服务商模型列表失败: {ProviderId}", requestedProviderId);
        _notificationService.ShowError(ModelDiscoveryStatus);
    }

    private async Task OpenUrlAsync(string url)
    {
        await SafeExecuteAsync(async () =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            await Task.CompletedTask;
        }, "打开链接");
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