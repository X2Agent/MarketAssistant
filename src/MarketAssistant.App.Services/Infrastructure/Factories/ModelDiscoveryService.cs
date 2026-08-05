using System.ClientModel;
using MarketAssistant.Infrastructure.Providers;
using OllamaSharp;
using OpenAI;

namespace MarketAssistant.Infrastructure.Factories;

public interface IModelDiscoveryService
{
    Task<IReadOnlyList<string>> ListModelsAsync(
        ModelProvider provider,
        string? apiKey,
        string? endpointOverride = null,
        CancellationToken cancellationToken = default);
}

public sealed class ModelDiscoveryService(IHttpClientFactory httpClientFactory) : IModelDiscoveryService
{
    public async Task<IReadOnlyList<string>> ListModelsAsync(
        ModelProvider provider,
        string? apiKey,
        string? endpointOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!provider.SupportsModelListing)
            return [];

        var endpoint = ResolveEndpoint(provider, endpointOverride);
        if (provider.Protocol == ModelApiProtocol.Ollama)
        {
            using var client = new OllamaApiClient(new Uri(endpoint));
            var models = await client.ListLocalModelsAsync(cancellationToken);
            return models
                .Select(model => model.Name ?? model.ModelName)
                .OfType<string>()
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(modelId => modelId)
                .ToList();
        }

        if (provider.ModelListingRequiresApiKey && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"服务商 {provider.DisplayName} 的模型列表接口需要 API Key");

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(30)
        };
        var clientOptions = string.IsNullOrWhiteSpace(apiKey)
            ? new OpenAIClient(new ApiKeyCredential("anonymous"), CreateAnonymousOptions(options))
            : new OpenAIClient(new ApiKeyCredential(apiKey), options);
        var modelsResult = await clientOptions.GetOpenAIModelClient().GetModelsAsync(cancellationToken);

        return modelsResult.Value
            .Select(model => model.Id)
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .Where(modelId => provider.GetProtocol(modelId) != ModelApiProtocol.Unsupported)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(modelId => modelId)
            .ToList();
    }

    private static string ResolveEndpoint(ModelProvider provider, string? endpointOverride)
    {
        var endpoint = provider.AllowsEndpointOverride && !string.IsNullOrWhiteSpace(endpointOverride)
            ? endpointOverride
            : provider.DefaultEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint) && provider.Protocol == ModelApiProtocol.Ollama)
            endpoint = "http://localhost:11434";

        if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/'), UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"服务商 {provider.DisplayName} 的 API Base URL 无效");
        }

        return endpointUri.AbsoluteUri.TrimEnd('/');
    }

    private OpenAIClientOptions CreateAnonymousOptions(OpenAIClientOptions options)
    {
        options.Transport = new AnonymousHttpClientPipelineTransport(
            httpClientFactory.CreateClient("AnonymousOpenAI"));
        return options;
    }
}
