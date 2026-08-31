using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Agents.Analysts;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 设置页 ViewModel 的模型服务商配置部分：API Key / 模型 ID / 端点、模型目录获取、分析师角色加载。
/// </summary>
public partial class SettingsPageViewModel
{
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
}
