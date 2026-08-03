namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 服务商适配器工厂。
/// </summary>
public interface IModelProviderAdapterFactory
{
    IModelProviderAdapter Create(ModelProvider provider);
}

/// <summary>
/// 通过共享 HttpClientFactory 创建协议适配器，统一模型发现请求的连接生命周期。
/// </summary>
public sealed class ModelProviderAdapterFactory : IModelProviderAdapterFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelProviderAdapterFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IModelProviderAdapter Create(ModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return provider.AdapterKind switch
        {
            ProviderAdapterKind.Ollama => new OllamaAdapter(provider, _httpClientFactory),
            _ => new OpenAICompatibleAdapter(provider, _httpClientFactory)
        };
    }
}
