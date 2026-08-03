using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Net.Http.Json;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// OpenAI 兼容协议适配器。覆盖所有支持 OpenAI API 兼容格式的服务商。
/// 包括硅基流动、DeepSeek、月之暗面、智谱、通义千问、百川、MiniMax、豆包、OpenAI。
/// Ollama 使用独立的 OllamaSharp SDK 适配器。
/// </summary>
public class OpenAICompatibleAdapter : IModelProviderAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelProvider Provider { get; }

    public OpenAICompatibleAdapter(
        ModelProvider provider,
        IHttpClientFactory httpClientFactory)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public IChatClient CreateChatClient(string? apiKey, string modelId, string? endpointOverride = null)
    {
        var (endpoint, key) = BuildConfig(apiKey, modelId, endpointOverride);

        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint),
                NetworkTimeout = TimeSpan.FromMinutes(3)
            }
        );

        return openAIClient.GetChatClient(modelId).AsIChatClient();
    }

    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(
        string apiKey, string modelId, string? endpointOverride = null)
    {
        var (endpoint, key) = BuildConfig(apiKey, modelId, endpointOverride);

        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint)
            }
        );

        return openAIClient.GetEmbeddingClient(modelId).AsIEmbeddingGenerator();
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(
        string? apiKey,
        string? endpointOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!Provider.SupportsModelListing)
            return [];

        var (endpoint, key) = BuildConfig(apiKey, null, endpointOverride);
        var http = _httpClientFactory.CreateClient("ModelDiscovery");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/models");
        request.Headers.Authorization = new("Bearer", key);
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var modelsResponse = await response.Content.ReadFromJsonAsync<OpenAIModelsResponse>(
            cancellationToken: cancellationToken);
        return modelsResponse?.Data?
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(Provider.IsModelSupported)
            .OrderBy(id => id)
            .ToList() ?? [];
    }

    private (string endpoint, string key) BuildConfig(
        string? apiKey,
        string? modelId,
        string? endpointOverride)
    {
        var endpoint = NormalizeEndpoint(endpointOverride ?? Provider.DefaultEndpoint);
        var requiresApiKey = string.IsNullOrWhiteSpace(modelId)
            ? Provider.RequiresApiKey
            : Provider.RequiresApiKeyForModel(modelId);
        var key = string.IsNullOrWhiteSpace(apiKey)
            ? (requiresApiKey ? "sk-placeholder" : "public")
            : apiKey;
        return (endpoint, key);
    }

    private string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"服务商 {Provider.DisplayName} 未配置 API Base URL");

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"服务商 {Provider.DisplayName} 的 API Base URL 无效");
        }

        return normalized.TrimEnd('/');
    }

    private sealed class OpenAIModelsResponse
    {
        public List<OpenAIModelItem>? Data { get; set; }
    }

    private sealed class OpenAIModelItem
    {
        public string Id { get; set; } = "";
    }
}
