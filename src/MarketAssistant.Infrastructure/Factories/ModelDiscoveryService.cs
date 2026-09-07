using System.Net.Http.Headers;
using System.Text.Json;
using MarketAssistant.Infrastructure.Providers;

namespace MarketAssistant.Infrastructure.Factories;

public interface IModelDiscoveryService
{
    Task<IReadOnlyList<string>> ListModelsAsync(
        ModelProvider provider,
        string? apiKey,
        string? endpointOverride = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 模型列表发现服务。模型列表接口统一按 OpenAI 兼容约定：有 API Key 加
/// <c>Authorization: Bearer</c>，无 Key 匿名请求（是否强制要求 Key 由
/// <see cref="ModelProvider.ModelListingRequiresApiKey"/> 控制）。
/// URL 由 API Base URL + <see cref="ModelProvider.ModelListingUrlPath"/> 拼接，
/// 响应解析兼容常见的 <c>data</c>、<c>models</c> 与根数组形状，模型标识支持
/// <c>id</c>、<c>name</c>、<c>model</c> 字段或字符串元素。发现阶段忠实返回服务商目录，
/// 不按当前聊天协议过滤；协议支持性在创建聊天客户端时单独校验。
/// 新增 OpenAI 兼容服务商只需在 Catalog 配置数据，无需改动本服务。
/// </summary>
public sealed class ModelDiscoveryService(IHttpClientFactory httpClientFactory) : IModelDiscoveryService
{
    private const string HttpClientName = "AnonymousOpenAI";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<string>> ListModelsAsync(
        ModelProvider provider,
        string? apiKey,
        string? endpointOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!provider.SupportsModelListing)
            return [];

        if (provider.ModelListingRequiresApiKey && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"服务商 {provider.DisplayName} 的模型列表接口需要 API Key");

        var effectiveOverride = provider.AllowsEndpointOverride ? endpointOverride : null;
        var endpoint = provider.ResolveEndpoint(effectiveOverride);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}{provider.ModelListingUrlPath}");
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // 每次调用通过工厂创建客户端，避免 Singleton 缓存 HttpClient 受 DNS 变化影响
        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
        return ExtractModelIds(doc.RootElement)
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 提取常见模型目录响应中的模型标识。
    /// </summary>
    private static IEnumerable<string> ExtractModelIds(JsonElement root)
    {
        var models = root.ValueKind == JsonValueKind.Array
            ? root
            : TryGetArray(root, "data", out var data)
                ? data
                : TryGetArray(root, "models", out var modelArray)
                    ? modelArray
                    : default;

        if (models.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in models.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return item.GetString()!;
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var propertyName in ModelIdentifierPropertyNames)
            {
                if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
                    continue;

                yield return value.GetString()!;
                break;
            }
        }
    }

    private static readonly string[] ModelIdentifierPropertyNames = ["id", "name", "model"];

    private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

}
