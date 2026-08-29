using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 基于 DI 容器的 RAG 基础设施提供者实现，首次调用时才触发对应单例链的构造。
/// </summary>
public sealed class RagInfrastructureProvider : IRagInfrastructureProvider
{
    private readonly IServiceProvider _serviceProvider;

    public RagInfrastructureProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IEmbeddingFactory GetEmbeddingFactory()
        => _serviceProvider.GetRequiredService<IEmbeddingFactory>();

    public VectorStore GetVectorStore()
        => _serviceProvider.GetRequiredService<VectorStore>();

    public IRagIngestionService GetIngestionService()
        => _serviceProvider.GetRequiredService<IRagIngestionService>();
}
