using Microsoft.Extensions.AI;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 服务商适配器接口。每个适配器负责创建对应协议的 IChatClient 和 IEmbeddingGenerator。
/// </summary>
public interface IModelProviderAdapter
{
    /// <summary>
    /// 服务商定义
    /// </summary>
    ModelProvider Provider { get; }

    /// <summary>
    /// 创建 ChatClient 实例
    /// </summary>
    /// <param name="apiKey">API 密钥（Ollama 等本地服务商可忽略）</param>
    /// <param name="modelId">模型 ID</param>
    /// <param name="endpointOverride">用户自定义的 endpoint 覆盖</param>
    IChatClient CreateChatClient(string? apiKey, string modelId, string? endpointOverride = null);

    /// <summary>
    /// 创建 Embedding 生成器（不支持 embedding 的服务商返回 null）
    /// </summary>
    IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(string apiKey, string modelId, string? endpointOverride = null);

    /// <summary>
    /// 从服务商 API 获取可用模型列表
    /// </summary>
    /// <param name="apiKey">API 密钥</param>
    /// <param name="endpointOverride">用户自定义的 endpoint 覆盖</param>
    /// <param name="cancellationToken">取消模型发现请求</param>
    /// <returns>模型 ID 列表</returns>
    Task<IReadOnlyList<string>> ListModelsAsync(
        string? apiKey,
        string? endpointOverride = null,
        CancellationToken cancellationToken = default);
}
