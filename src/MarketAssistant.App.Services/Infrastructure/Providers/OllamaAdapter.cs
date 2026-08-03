using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Net.Http.Json;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// Ollama 本地部署适配器。使用 OllamaSharp SDK（原生 Ollama API）。
/// 无需 API Key，通过 OllamaApiClient 创建 IChatClient。
/// </summary>
public class OllamaAdapter : IModelProviderAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelProvider Provider { get; }

    public OllamaAdapter(
        ModelProvider provider,
        IHttpClientFactory httpClientFactory)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public IChatClient CreateChatClient(string? apiKey, string modelId, string? endpointOverride = null)
    {
        var ollamaClient = CreateClient(modelId, endpointOverride);

        // Function Invocation 由上层 ChatClientAgent 统一拥有，避免双重工具调用循环。
        return ollamaClient;
    }

    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(
        string apiKey, string modelId, string? endpointOverride = null)
    {
        var ollamaClient = CreateClient(modelId, endpointOverride);
        return ollamaClient;
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(
        string? apiKey,
        string? endpointOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!Provider.SupportsModelListing)
            return [];

        var endpoint = endpointOverride ?? Provider.DefaultEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = "http://localhost:11434";

        var baseUri = endpoint.TrimEnd('/');
        var http = _httpClientFactory.CreateClient("ModelDiscovery");
        var response = await http.GetFromJsonAsync<OllamaTagsResponse>(
            $"{baseUri}/api/tags",
            cancellationToken);
        return response?.Models?
            .Select(m => m.Name ?? m.Model ?? "")
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name)
            .ToList() ?? [];
    }

    private OllamaApiClient CreateClient(string modelId, string? endpointOverride)
    {
        var endpoint = endpointOverride ?? Provider.DefaultEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = "http://localhost:11434";

        var uri = new Uri(endpoint.TrimEnd('/'));
        return new OllamaApiClient(uri, modelId);
    }

    private sealed class OllamaTagsResponse
    {
        public List<OllamaModelInfo>? Models { get; set; }
    }

    private sealed class OllamaModelInfo
    {
        public string? Name { get; set; }
        public string? Model { get; set; }
    }
}
