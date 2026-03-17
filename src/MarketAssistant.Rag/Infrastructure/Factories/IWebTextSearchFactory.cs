using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Infrastructure.Factories;

public interface IWebTextSearchFactory
{
    ITextSearch? Create();
}
