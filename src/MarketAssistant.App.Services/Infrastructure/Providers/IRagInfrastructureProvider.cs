using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// RAG 基础设施提供者：将向量化所需的重依赖收敛为具名契约并延迟解析，
/// 避免浏览设置页等场景触发嵌入/向量存储/摄取链路的同步构造。
/// </summary>
public interface IRagInfrastructureProvider
{
    IEmbeddingFactory GetEmbeddingFactory();

    VectorStore GetVectorStore();

    IRagIngestionService GetIngestionService();
}
