using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 带降级功能的重排服务装饰器
/// 优先使用主要重排服务，失败时自动降级到备用服务
/// </summary>
public class FallbackRerankerService : IRerankerService
{
    private readonly IRerankerService _primaryReranker;
    private readonly IRerankerService _fallbackReranker;
    private readonly ILogger<FallbackRerankerService> _logger;

    public FallbackRerankerService(
        [FromKeyedServices("primary")] IRerankerService primaryReranker,
        [FromKeyedServices("fallback")] IRerankerService fallbackReranker,
        ILogger<FallbackRerankerService> logger)
    {
        _primaryReranker = primaryReranker;
        _fallbackReranker = fallbackReranker;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TextSearchResult>> RerankAsync(
        string query,
        IEnumerable<TextSearchResult> items,
        CancellationToken cancellationToken = default)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return list;

        try
        {
            // 尝试使用主要重排服务
            var result = await _primaryReranker.RerankAsync(query, list, cancellationToken);
            _logger.LogDebug("Primary reranker succeeded");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary reranker failed, falling back to secondary reranker");

            try
            {
                return await _fallbackReranker.RerankAsync(query, list, cancellationToken);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallback reranker also failed");
                // 如果所有重排服务都失败，返回原始顺序
                return list;
            }
        }
    }
}
