using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// 对召回结果进行重排的抽象接口
/// </summary>
public interface IRerankerService
{
    Task<IReadOnlyList<TextSearchResult>> RerankAsync(string query, IEnumerable<TextSearchResult> items, CancellationToken cancellationToken = default);
}
