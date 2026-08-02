using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Brave;
using Microsoft.SemanticKernel.Plugins.Web.Tavily;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// Web 搜索服务：按用户设置选择 Bing/Brave/Tavily 提供商并执行搜索。
/// </summary>
public class WebSearchService(IUserSettingService userSettingService, ILogger<WebSearchService> logger) : IWebSearchService
{
    public async Task<IReadOnlyList<TextSearchResult>> SearchAsync(
        string query, int top, CancellationToken cancellationToken = default)
    {
        var setting = userSettingService.CurrentSetting;
        if (!setting.EnableWebSearch || string.IsNullOrWhiteSpace(setting.WebSearchApiKey))
        {
            logger.LogDebug("网络搜索未启用或未配置密钥，跳过搜索");
            return [];
        }

        try
        {
            var results = setting.WebSearchProvider?.ToLowerInvariant() switch
            {
                "bing" => await SearchAsync(new BingTextSearch(apiKey: setting.WebSearchApiKey), query, top, cancellationToken),
                "brave" => await SearchAsync(new BraveTextSearch(apiKey: setting.WebSearchApiKey), query, top, cancellationToken),
                "tavily" => await SearchAsync(new TavilyTextSearch(apiKey: setting.WebSearchApiKey), query, top, cancellationToken),
                _ => []
            };
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "网络搜索失败: {Query}", query);
            return [];
        }
    }

    private static async Task<IReadOnlyList<TextSearchResult>> SearchAsync<TRecord>(
        ITextSearch<TRecord> search, string query, int top, CancellationToken cancellationToken)
        where TRecord : class
    {
        var result = await search.GetTextSearchResultsAsync(
            query,
            new TextSearchOptions<TRecord> { Top = top },
            cancellationToken);

        var results = new List<TextSearchResult>();
        await foreach (var r in result.Results.WithCancellation(cancellationToken))
        {
            results.Add(r);
        }
        return results;
    }
}
