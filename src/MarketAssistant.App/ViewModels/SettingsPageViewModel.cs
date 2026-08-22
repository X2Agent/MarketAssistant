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

/// <summary>
/// 设置页ViewModel
/// </summary>
public partial class SettingsPageViewModel : ViewModelBase, IDisposable
{
    // RAG 与交易重依赖通过工厂延迟解析：仅在向量化/保存时实例化，
    // 避免首次进入设置页触发整条交易与 RAG 单例链的同步构造
    private readonly Func<IRagIngestionService> _ragIngestionServiceFactory;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly IModelDiscoveryService _modelDiscoveryService;
    private readonly Func<IEmbeddingFactory> _embeddingFactoryFactory;
    private readonly Func<VectorStore> _vectorStoreFactory;
    private readonly Services.Market.MarketContext _marketContext;
    private readonly TradingEnvironmentService _tradingEnvironmentService;
    private readonly Func<MarketMonitor> _marketMonitorFactory;
    private readonly IDialogService _dialogService;
    private IStorageProvider? _storageProvider;
    private bool _isInitializingProvider;
    private CancellationTokenSource? _modelFetchCancellationTokenSource;

    // UserSetting对象，包含所有用户设置
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

    // 模型列表 - ViewModel特有属性
    [ObservableProperty]
    private ObservableCollection<string> _models = [];

    // 服务商列表
    public List<ModelProvider> Providers => ModelProviderCatalog.Providers.ToList();

    // 当前选中的服务商
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

    // 当前服务商的 API Key 获取链接
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

    // 当前服务商是否支持在线获取模型列表
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

    // 是否正在加载模型列表
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

    // 分析师角色列表
    [ObservableProperty]
    private ObservableCollection<AnalystRoleViewModel> _analystRoles = new();

    // 判断知识库目录是否有效 - 计算属性
    public bool IsKnowledgeDirectoryValid => !string.IsNullOrEmpty(UserSetting.KnowledgeFileDirectory) && Directory.Exists(UserSetting.KnowledgeFileDirectory);

    // 是否正在向量化
    [ObservableProperty]
    private bool _isVectorizing;

    // 向量化进度（0-100）
    [ObservableProperty]
    private int _vectorizingProgress;

    // 向量化进度文本
    [ObservableProperty]
    private string _vectorizingProgressText = "";

    // Web Search服务商列表
    public List<string> WebSearchProviders { get; } = new List<string> { "Bing", "Brave", "Tavily" };

    // 风险承受能力选项
    public List<RiskToleranceLevel> RiskToleranceOptions { get; } = Enum.GetValues<RiskToleranceLevel>().ToList();

    // 投资期限选项
    public List<InvestmentHorizonType> InvestmentHorizonOptions { get; } = Enum.GetValues<InvestmentHorizonType>().ToList();

    // 虚拟币交易模式选项
    public List<CryptoTradingMode> CryptoTradingModes { get; } = Enum.GetValues<CryptoTradingMode>().ToList();

    // API密钥获取URL
    public string ZhiTuApiUrl { get; } = "https://www.zhituapi.com/gettoken.html";
    public string CoinGeckoApiUrl { get; } = "https://www.coingecko.com/en/api";
    public string JinaApiUrl { get; } = "https://jina.ai/embeddings";

    /// <summary>
    /// 主题：跟随系统
    /// </summary>
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

    /// <summary>
    /// 主题：浅色
    /// </summary>
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

    /// <summary>
    /// 主题：深色
    /// </summary>
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

    /// <summary>
    /// 是否为A股市场
    /// </summary>
    public bool IsAShareMarket
    {
        get => UserSetting.CurrentMarketType == MarketType.AShare;
        set
        {
            if (value && UserSetting.CurrentMarketType != MarketType.AShare)
            {
                // 仅修改本地 UserSetting，不立即调用 SwitchMarket
                // 避免触发 MainWindowViewModel 重建导航，导致正在编辑的设置丢失
                // 实际市场切换统一在 Save() 中执行
                UserSetting.CurrentMarketType = MarketType.AShare;
                Logger?.LogInformation("市场选择已改为: A股（保存后生效）");
            }
        }
    }

    /// <summary>
    /// 是否为虚拟币市场
    /// </summary>
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

    /// <summary>
    /// 是否为 Binance 实盘现货模式
    /// </summary>
    public bool IsLiveSpotTradingMode => UserSetting.CryptoTradingMode == CryptoTradingMode.LiveSpot;

    /// <summary>
    /// 是否为实盘模式（现货或合约，共用同一套 API Key）
    /// </summary>
    public bool IsLiveTradingMode => IsLiveSpotTradingMode || IsLiveFuturesTradingMode;

    /// <summary>
    /// 是否为 Binance 实盘合约模式
    /// </summary>
    public bool IsLiveFuturesTradingMode => UserSetting.CryptoTradingMode == CryptoTradingMode.LiveFutures;

    /// <summary>
    /// 是否为 Binance Futures Testnet 模式
    /// </summary>
    public bool IsFuturesTestnetTradingMode => UserSetting.CryptoTradingMode == CryptoTradingMode.BinanceFuturesTestnet;

    /// <summary>
    /// 是否为合约模式（实盘或 Testnet）
    /// </summary>
    public bool IsFuturesTradingMode => IsLiveFuturesTradingMode || IsFuturesTestnetTradingMode;

    /// <summary>
    /// 是否为Bing搜索平台
    /// </summary>
    public bool IsBingProvider
    {
        get => UserSetting.WebSearchProvider == "Bing";
        set
        {
            if (value)
                UserSetting.WebSearchProvider = "Bing";
        }
    }

    /// <summary>
    /// 是否为Brave搜索平台
    /// </summary>
    public bool IsBraveProvider
    {
        get => UserSetting.WebSearchProvider == "Brave";
        set
        {
            if (value)
                UserSetting.WebSearchProvider = "Brave";
        }
    }

    /// <summary>
    /// 是否为Tavily搜索平台
    /// </summary>
    public bool IsTavilyProvider
    {
        get => UserSetting.WebSearchProvider == "Tavily";
        set
        {
            if (value)
                UserSetting.WebSearchProvider = "Tavily";
        }
    }

    /// <summary>
    /// 构造函数（使用依赖注入）
    /// </summary>
    public SettingsPageViewModel(
        Func<IRagIngestionService> ragIngestionServiceFactory,
        INotificationService notificationService,
        IUserSettingService userSettingService,
        IModelDiscoveryService modelDiscoveryService,
        Func<IEmbeddingFactory> embeddingFactoryFactory,
        Func<VectorStore> vectorStoreFactory,
        Services.Market.MarketContext marketContext,
        TradingEnvironmentService tradingEnvironmentService,
        Func<MarketMonitor> marketMonitorFactory,
        IDialogService dialogService,
        ILogger<SettingsPageViewModel> logger) : base(logger)
    {
        _ragIngestionServiceFactory = ragIngestionServiceFactory;
        _notificationService = notificationService;
        _userSettingService = userSettingService;
        _modelDiscoveryService = modelDiscoveryService;
        _embeddingFactoryFactory = embeddingFactoryFactory;
        _vectorStoreFactory = vectorStoreFactory;
        _marketContext = marketContext;
        _tradingEnvironmentService = tradingEnvironmentService;
        _marketMonitorFactory = marketMonitorFactory;
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
        // 加载用户设置（OnUserSettingChanged 会自动订阅 PropertyChanged）
        UserSetting = _userSettingService.CurrentSetting;

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
        // 加载分析师角色
        LoadAnalystRoles();
        // 应用保存的主题
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

            // 使用类名作为ID
            var id = type.Name;

            // 从设置中获取启用状态
            var isEnabled = false;
            if (UserSetting.EnabledAnalystRoles.TryGetValue(id, out var enabled))
            {
                isEnabled = enabled;
            }

            // 强制必需的角色为启用
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

    /// <summary>
    /// 向量化文档
    /// </summary>
    [RelayCommand]
    private async Task VectorizeDocuments()
    {
        if (!IsKnowledgeDirectoryValid)
        {
            _notificationService.ShowWarning("知识库目录无效，请先选择有效的目录");
            Logger?.LogWarning("知识库目录无效，无法进行向量化");
            return;
        }

        try
        {
            IsVectorizing = true;
            VectorizingProgress = 0;
            VectorizingProgressText = "准备中...";

            Logger?.LogInformation("开始向量化知识库目录: {Directory}", UserSetting.KnowledgeFileDirectory);

            // 创建嵌入生成器（只在实际需要时创建）
            var embeddingGenerator = _embeddingFactoryFactory().Create();

            // 使用 UserSetting 中定义的集合名称
            var collectionName = UserSetting.VectorCollectionName;
            var collection = _vectorStoreFactory().GetCollection<string, TextParagraph>(collectionName);
            await collection.EnsureCollectionExistsAsync();
            Logger?.LogInformation("使用向量集合: {CollectionName}", collectionName);

            // 支持的文件扩展名：PDF、DOCX、Markdown
            var supportedExtensions = new[] { ".pdf", ".docx", ".md" };

            // 扫描目录获取所有支持的文件
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

            var ragIngestionService = _ragIngestionServiceFactory();
            var successCount = 0;
            var failedCount = 0;
            var failedFiles = new List<string>();

            // 逐个处理文件
            for (int i = 0; i < totalFiles; i++)
            {
                var file = files[i];
                var fileName = Path.GetFileName(file);
                var fileExtension = Path.GetExtension(file).ToUpperInvariant();

                try
                {
                    // 更新进度
                    var currentIndex = i + 1;
                    VectorizingProgress = (int)((double)currentIndex / totalFiles * 100);
                    VectorizingProgressText = $"正在处理 {currentIndex}/{totalFiles}: {fileName}";

                    Logger?.LogInformation("正在处理 ({Index}/{Total}): {FileName} [{Extension}]",
                        currentIndex, totalFiles, fileName, fileExtension);

                    // 执行向量化
                    await ragIngestionService.IngestFileAsync(collection, file, embeddingGenerator);

                    successCount++;
                    Logger?.LogInformation("✓ 成功向量化: {FileName}", fileName);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    failedFiles.Add(fileName);
                    Logger?.LogError(ex, "✗ 向量化失败: {FileName} - {ErrorMessage}", fileName, ex.Message);

                    // 单个文件失败不中断整体流程，继续处理下一个
                }
            }

            // 显示完成消息
            VectorizingProgress = 100;
            if (failedCount == 0)
            {
                VectorizingProgressText = $"✅ 全部完成！共 {successCount} 个文件";
                _notificationService.ShowSuccess($"✅ 所有文档向量化完成！\n成功处理 {successCount} 个文件");
                Logger?.LogInformation("向量化完成：成功 {Success}/{Total} 个", successCount, totalFiles);
            }
            else
            {
                VectorizingProgressText = $"⚠️ 完成（部分失败）: {successCount} 成功, {failedCount} 失败";
                var failedList = string.Join("\n- ", failedFiles.Take(5));
                if (failedFiles.Count > 5)
                {
                    failedList += $"\n... 还有 {failedFiles.Count - 5} 个";
                }

                _notificationService.ShowWarning(
                    $"向量化完成：\n✓ 成功 {successCount} 个\n✗ 失败 {failedCount} 个\n\n失败文件：\n- {failedList}");

                Logger?.LogWarning("向量化完成：成功 {Success} 个，失败 {Failed} 个，总计 {Total} 个",
                    successCount, failedCount, totalFiles);
            }
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
        }
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            // 同步分析师角色设置
            foreach (var role in AnalystRoles)
            {
                UserSetting.EnabledAnalystRoles[role.Id] = role.IsEnabled;
            }

            // 监控运行中切换到实盘会使后续订单进入真实账户，必须二次确认。
            var targetMode = UserSetting.CryptoTradingMode;
            if (TradingEnvironmentService.RequiresLiveModeConfirmation(
                    _tradingEnvironmentService.CurrentMode,
                    targetMode,
                    _marketMonitorFactory().IsRunning))
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

            _userSettingService.UpdateSettings(UserSetting);
            await _tradingEnvironmentService.ApplyModeAsync(UserSetting.CryptoTradingMode);
            _notificationService.ShowSuccess("设置已保存");
            Logger?.LogInformation("保存设置，市场类型：{MarketType}，交易模式：{TradingMode}",
                UserSetting.CurrentMarketType,
                UserSetting.CryptoTradingMode);
        }, "保存设置");
    }

    /// <summary>
    /// 重置设置为默认值
    /// </summary>
    [RelayCommand]
    private async Task Reset()
    {
        await SafeExecuteAsync(async () =>
        {
            _userSettingService.ResetSettings();
            UserSetting = _userSettingService.CurrentSetting;

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
            LoadAnalystRoles(); // 重新加载角色
            _notificationService.ShowSuccess("设置已重置为默认值");
            Logger?.LogInformation("重置设置为默认值");
        }, "重置设置");
    }

    /// <summary>
    /// 导航到MCP服务器配置页面
    /// </summary>
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

        _modelFetchCancellationTokenSource?.Cancel();
        _modelFetchCancellationTokenSource?.Dispose();
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

    /// <summary>
    /// 打开URL
    /// </summary>
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
        _modelFetchCancellationTokenSource?.Dispose();
        _modelFetchCancellationTokenSource = null;

        // 取消 UserSetting.PropertyChanged 订阅，避免 Singleton 持有已释放 ViewModel 的引用
        if (UserSetting is not null)
            UserSetting.PropertyChanged -= ForwardComputedProperties;

        GC.SuppressFinalize(this);
    }
}