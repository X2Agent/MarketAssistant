using System.Security.Cryptography;
using System.Text;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Polly;
using Polly.Retry;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// ChatClient 工厂接口
/// 负责创建和管理底层的 IChatClient 实例
/// </summary>
public interface IChatClientFactory : IDisposable
{
    /// <summary>
    /// 创建配置好的 ChatClient 实例。
    /// </summary>
    IChatClient CreateClient();

    /// <summary>
    /// 创建绑定不可变模型配置快照的 ChatClient Runtime。
    /// </summary>
    ChatClientRuntime CreateRuntime();
}

/// <summary>
/// ChatClient 与其不可变模型配置快照。
/// </summary>
public sealed record ChatClientRuntime(
    IChatClient Client,
    string ProviderId,
    string ModelId,
    string Endpoint,
    string ConfigurationFingerprint,
    int? ContextWindowTokens);

/// <summary>
/// ChatClient 工厂实现
/// 根据用户配置的服务商创建对应的 IChatClient，并附加 LLM 瞬态错误重试管道
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    private const int MaxCachedRuntimes = 16;
    private static readonly TimeSpan ErrorCooldown = TimeSpan.FromSeconds(30);

    private static readonly ResiliencePipeline LlmRetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(2),
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>(ex => IsNetworkTimeout(ex))
        })
        .Build();

    private static bool IsNetworkTimeout(TaskCanceledException ex)
    {
        if (ex.InnerException is TimeoutException) return true;
        if (ex.CancellationToken.IsCancellationRequested) return false;
        return ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private readonly IUserSettingService _userSettingService;
    private readonly IModelProviderAdapterFactory _adapterFactory;
    private readonly object _lock = new();
    private readonly Dictionary<ModelRuntimeKey, IChatClient> _clients = [];
    private readonly Dictionary<ModelRuntimeKey, CachedError> _errors = [];
    private bool _disposed;

    public ChatClientFactory(
        IUserSettingService userSettingService,
        IModelProviderAdapterFactory adapterFactory)
    {
        _userSettingService = userSettingService;
        _adapterFactory = adapterFactory;
    }

    public IChatClient CreateClient() => CreateRuntime().Client;

    public ChatClientRuntime CreateRuntime()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var userSetting = _userSettingService.CurrentSetting;
            var providerId = userSetting.ProviderId;
            var modelId = userSetting.ModelId;
            var provider = ModelProviderCatalog.GetProvider(providerId)
                ?? throw new FriendlyException($"未知的服务商: {providerId}");
            var apiKey = userSetting.ProviderApiKeys.TryGetValue(providerId, out var key) ? key : string.Empty;
            var endpoint = ResolveEndpoint(provider, userSetting.Endpoint);
            var runtimeKey = new ModelRuntimeKey(providerId, modelId, endpoint, ComputeSecretFingerprint(apiKey));
            var configurationFingerprint = ComputeConfigurationFingerprint(runtimeKey);

            if (_clients.TryGetValue(runtimeKey, out var cachedClient))
            {
                return new ChatClientRuntime(
                    cachedClient,
                    providerId,
                    modelId,
                    endpoint,
                    configurationFingerprint,
                    provider.GetContextWindowTokens(modelId));
            }

            if (_errors.TryGetValue(runtimeKey, out var cachedError) &&
                DateTime.UtcNow - cachedError.Timestamp < ErrorCooldown)
            {
                throw new FriendlyException(cachedError.Message);
            }

            try
            {
                if (string.IsNullOrWhiteSpace(modelId))
                    throw new FriendlyException("AI 功能未配置:请先在设置页面选择 AI 模型");

                if (!provider.IsModelSupported(modelId))
                {
                    throw new FriendlyException(
                        $"模型 {modelId} 不兼容 {provider.DisplayName} 当前使用的协议适配器");
                }

                if (provider.RequiresApiKeyForModel(modelId) && string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new FriendlyException(
                        $"AI 功能未配置：服务商 {provider.DisplayName} 的模型 {modelId} 需要 API Key，请先在设置页面配置");
                }

                if (_clients.Count >= MaxCachedRuntimes)
                {
                    throw new FriendlyException(
                        $"本次应用运行已使用 {MaxCachedRuntimes} 组不同的模型配置。" +
                        "为避免释放仍被会话引用的客户端，请重启应用后再切换新配置");
                }

                var adapter = _adapterFactory.Create(provider);
                var rawClient = adapter.CreateChatClient(apiKey, modelId, endpoint);
                var resilientClient = new ResilientChatClient(rawClient, LlmRetryPipeline);
                _clients.Add(runtimeKey, resilientClient);
                _errors.Remove(runtimeKey);
                return new ChatClientRuntime(
                    resilientClient,
                    providerId,
                    modelId,
                    endpoint,
                    configurationFingerprint,
                    provider.GetContextWindowTokens(modelId));
            }
            catch (FriendlyException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _errors[runtimeKey] = new CachedError(ex.Message, DateTime.UtcNow);
                throw new FriendlyException($"创建 AI 客户端失败: {ex.Message}", ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            foreach (var client in _clients.Values)
                client.Dispose();

            _clients.Clear();
            _errors.Clear();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private static string ResolveEndpoint(ModelProvider provider, string configuredEndpoint)
    {
        if (string.IsNullOrWhiteSpace(provider.DefaultEndpoint))
            return configuredEndpoint.Trim().TrimEnd('/');

        return provider.DefaultEndpoint.Trim().TrimEnd('/');
    }

    private static string ComputeSecretFingerprint(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return string.Empty;

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static string ComputeConfigurationFingerprint(ModelRuntimeKey runtimeKey)
    {
        var canonicalValue = string.Join(
            '\n',
            runtimeKey.ProviderId,
            runtimeKey.ModelId,
            runtimeKey.Endpoint,
            runtimeKey.ApiKeyFingerprint);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue)));
    }

    private sealed record ModelRuntimeKey(
        string ProviderId,
        string ModelId,
        string Endpoint,
        string ApiKeyFingerprint);

    private sealed record CachedError(string Message, DateTime Timestamp);
}
