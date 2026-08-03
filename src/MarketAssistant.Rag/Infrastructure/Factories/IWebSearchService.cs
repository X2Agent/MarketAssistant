using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// Web 搜索执行契约：根据用户设置选择搜索提供商（Bing/Brave/Tavily），
/// 统一返回 <see cref="TextSearchResult"/> 结果集，屏蔽各提供商泛型记录类型的差异。
/// </summary>
public interface IWebSearchService
{
    Task<IReadOnlyList<TextSearchResult>> SearchAsync(
        string query, int top, CancellationToken cancellationToken = default);
}
