using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using OpenAI;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 嵌入生成器工厂：按 (Endpoint, API Key 指纹, ModelId) 缓存底层客户端，
/// 避免每次检索/向量化都新建 OpenAIClient 造成连接 churn。
/// </summary>
public class EmbeddingFactory : IEmbeddingFactory, IDisposable
{
    private readonly IUserSettingService _userSettingService;
    private readonly ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _generators = new();
    private bool _disposed;

    public EmbeddingFactory(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
    }

    public IEmbeddingGenerator<string, Embedding<float>> Create()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var userSetting = _userSettingService.CurrentSetting;
        var modelId = userSetting.EmbeddingModelId;
        var apiKey = userSetting.EmbeddingApiKey;
        var endpoint = userSetting.EmbeddingEndpoint;

        if (string.IsNullOrWhiteSpace(modelId))
            throw new FriendlyException("嵌入模型ID不能为空");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new FriendlyException("嵌入API密钥不能为空");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            throw new FriendlyException("嵌入服务 Endpoint 无效");
        }

        // Key 不含明文密钥（只含指纹），缓存实例不泄漏密钥字符串本身
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        var cacheKey = $"{endpointUri.AbsoluteUri}|{fingerprint}|{modelId}";

        return _generators.GetOrAdd(cacheKey, _ => CreateGenerator(endpointUri, modelId, apiKey));
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(Uri endpointUri, string modelId, string apiKey)
    {
        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = endpointUri,
                NetworkTimeout = TimeSpan.FromMinutes(3),
                // 显式对齐 Chat 路径的 3 次重试，不依赖 SDK 默认值
                RetryPolicy = new ClientRetryPolicy(3)
            });
        return client.GetEmbeddingClient(modelId).AsIEmbeddingGenerator();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var generator in _generators.Values)
            (generator as IDisposable)?.Dispose();
        _generators.Clear();

        GC.SuppressFinalize(this);
    }
}
