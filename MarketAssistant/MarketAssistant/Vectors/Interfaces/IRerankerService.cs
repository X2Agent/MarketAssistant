using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// ���ٻؽ���������ŵĳ���ӿ�
/// </summary>
public interface IRerankerService
{
    Task<IReadOnlyList<TextSearchResult>> RerankAsync(string query, IEnumerable<TextSearchResult> items, CancellationToken cancellationToken = default);
}
