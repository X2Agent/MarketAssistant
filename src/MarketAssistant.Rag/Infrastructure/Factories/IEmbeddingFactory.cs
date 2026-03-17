using Microsoft.Extensions.AI;

namespace MarketAssistant.Infrastructure.Factories;

public interface IEmbeddingFactory
{
    IEmbeddingGenerator<string, Embedding<float>> Create();
}
