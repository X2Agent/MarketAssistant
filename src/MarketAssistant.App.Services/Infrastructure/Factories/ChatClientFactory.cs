using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using OllamaSharp;
using OpenAI;
using Polly;

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
    StructuredOutputMode StructuredOutputMode);

/// <summary>
/// ChatClient 工厂实现
/// 根据用户配置创建并缓存官方 SDK 提供的 IChatClient。
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    private const int MaxCachedRuntimes = 16;

    private readonly IUserSettingService _userSettingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _lock = new();
    private readonly Dictionary<ModelRuntimeKey, IChatClient> _clients = [];
    private bool _disposed;

    public ChatClientFactory(
        IUserSettingService userSettingService,
        IHttpClientFactory httpClientFactory)
    {
        _userSettingService = userSettingService;
        _httpClientFactory = httpClientFactory;
    }

    public IChatClient CreateClient() => CreateRuntime().Client;

    public ChatClientRuntime CreateRuntime()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var userSetting = _userSettingService.CurrentSetting;
            var providerId = userSetting.ProviderId;
            var modelId = userSetting.ProviderModelIds.GetValueOrDefault(providerId, string.Empty);
            var provider = ModelProviderCatalog.GetProvider(providerId)
                ?? throw new FriendlyException($"未知的服务商: {providerId}");
            var configuredApiKey = userSetting.ProviderApiKeys.TryGetValue(providerId, out var key) ? key : string.Empty;
            var apiKey = provider.RequiresApiKey ? configuredApiKey : string.Empty;
            var endpointOverride = provider.AllowsEndpointOverride
                ? userSetting.ProviderEndpoints.GetValueOrDefault(providerId, string.Empty)
                : string.Empty;
            var endpoint = provider.ResolveEndpoint(endpointOverride);
            var structuredOutputMode = provider.StructuredOutputMode;
            var runtimeKey = new ModelRuntimeKey(
                providerId,
                modelId,
                endpoint,
                ComputeSecretFingerprint(apiKey));
            var configurationFingerprint = ComputeConfigurationFingerprint(runtimeKey);

            if (_clients.TryGetValue(runtimeKey, out var cachedClient))
            {
                return new ChatClientRuntime(
                    cachedClient,
                    providerId,
                    modelId,
                    endpoint,
                    configurationFingerprint,
                    structuredOutputMode);
            }

            try
            {
                if (string.IsNullOrWhiteSpace(modelId))
                    throw new FriendlyException("AI 功能未配置:请先在设置页面选择 AI 模型");

                if (provider.GetProtocol(modelId) == ModelApiProtocol.Unsupported)
                {
                    throw new FriendlyException(
                        $"模型 {modelId} 当前使用的 API 协议尚未接入 {provider.DisplayName}");
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

                var client = CreateClient(provider, modelId, apiKey, endpoint);
                _clients.Add(runtimeKey, client);
                return new ChatClientRuntime(
                    client,
                    providerId,
                    modelId,
                    endpoint,
                    configurationFingerprint,
                    structuredOutputMode);
            }
            catch (FriendlyException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FriendlyException($"创建 AI 客户端失败: {ex.Message}", ex);
            }
        }
    }

    protected virtual IChatClient CreateClient(
        ModelProvider provider,
        string modelId,
        string apiKey,
        string endpoint)
    {
        return provider.GetProtocol(modelId) switch
        {
            ModelApiProtocol.Ollama => CreateOllamaClient(endpoint, modelId),
            ModelApiProtocol.OpenAIChatCompletions => CreateOpenAIClient(apiKey, endpoint)
                .GetChatClient(modelId)
                .AsIChatClient(),
            _ => throw new FriendlyException($"模型 {modelId} 的 API 协议暂不受支持")
        };
    }

    /// <summary>
    /// OllamaSharp 不自带重试；用 Microsoft.Extensions.Http.Resilience 的标准重试管道
    /// 补齐与 OpenAI 协议（SDK 内建重试）对齐的弹性能力，并统一 3 分钟网络超时。
    /// </summary>
    private static OllamaApiClient CreateOllamaClient(string endpoint, string modelId)
    {
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();

        var httpClient = new HttpClient(new ResilienceHandler(pipeline))
        {
            BaseAddress = new Uri(endpoint),
            Timeout = TimeSpan.FromMinutes(3)
        };

        return new OllamaApiClient(httpClient, modelId);
    }

    private OpenAIClient CreateOpenAIClient(string apiKey, string endpoint)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(3)
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            options.Transport = new AnonymousHttpClientPipelineTransport(
                _httpClientFactory.CreateClient("AnonymousOpenAI"));
            return new OpenAIClient(new ApiKeyCredential("anonymous"), options);
        }

        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
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
            _disposed = true;
        }

        GC.SuppressFinalize(this);
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
}
