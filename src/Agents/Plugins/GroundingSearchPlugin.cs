using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Data;
using System.ComponentModel;

namespace MarketAssistant.Agents.Plugins;

/// <summary>
/// 智能搜索插件，根据用户设置自动选择最适合的搜索策略：
/// - 仅知识库：当用户启用知识库但禁用网络搜索时
/// - 仅网络搜索：当用户禁用知识库但启用网络搜索时  
/// - 混合搜索：当用户同时启用知识库和网络搜索时
/// - 空结果：当用户都未启用时，返回空结果
/// </summary>
public class GroundingSearchPlugin
{
    private readonly IRetrievalOrchestrator _orchestrator;
    private readonly IWebTextSearchFactory _webTextSearchFactory;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<GroundingSearchPlugin> _logger;

    public GroundingSearchPlugin(
        IRetrievalOrchestrator orchestrator,
        IWebTextSearchFactory webTextSearchFactory,
        IUserSettingService userSettingService,
        ILogger<GroundingSearchPlugin> logger)
    {
        _orchestrator = orchestrator;
        _webTextSearchFactory = webTextSearchFactory;
        _userSettingService = userSettingService;
        _logger = logger;
    }

    [Description("信息搜索：当现有信息缺失、需实时补充或更新动态时使用。返回格式化证据片段，含 Name/Link/Value。")]
    public async Task<List<TextSearchResult>> SearchAsync(
        [Description("搜索的查询语句或关键词，如'公司名 财务数据'、'行业 趋势变化'等")] string query,
        [Description("返回结果数量，建议3-6个")] int top = 6)
    {
        // 参数约束：避免极端模式使用和极端参数
        if (top <= 0) top = 3;
        if (top > 6) top = 6;

        var userSetting = _userSettingService.CurrentSetting;
        var hasKnowledgeEnabled = userSetting.LoadKnowledge;
        var hasWebSearchEnabled = userSetting.EnableWebSearch && !string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey);

        _logger.LogDebug("搜索配置 - 知识库: {KnowledgeEnabled}, 网络搜索: {WebEnabled}",
            hasKnowledgeEnabled, hasWebSearchEnabled);

        try
        {
            var searchResults = await ExecuteSearchStrategy(query, hasKnowledgeEnabled, hasWebSearchEnabled, top);
            return searchResults.Take(top).ToList();
            //return new KernelSearchResults<TextSearchResult>(AsAsync(searchResults.Take(top)), searchResults.Count, new Dictionary<string, object?>
            //{
            //    {"query", query}
            //});
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索执行失败: {Query}", query);
            return [];
        }
    }

    /// <summary>
    /// 根据配置执行对应的搜索策略
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteSearchStrategy(
        string query,
        bool hasKnowledgeEnabled,
        bool hasWebSearchEnabled,
        int top)
    {
        _logger.LogInformation("执行搜索策略 - 知识库: {Knowledge}, 网络: {Web}, 查询: {Query}",
            hasKnowledgeEnabled, hasWebSearchEnabled, query);

        try
        {
            // 并行执行启用的搜索方式
            var tasks = new List<Task<IReadOnlyList<TextSearchResult>>>();

            if (hasKnowledgeEnabled)
            {
                tasks.Add(ExecuteKnowledgeSearch(query, top));
            }

            if (hasWebSearchEnabled)
            {
                tasks.Add(ExecuteWebSearch(query, top));
            }

            // 如果没有启用任何搜索方式
            if (tasks.Count == 0)
            {
                return [];
            }

            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);

            // 合并所有结果
            return CombineResults(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索策略执行失败: {Query}", query);
            return [];
        }
    }

    /// <summary>
    /// 执行知识库搜索
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteKnowledgeSearch(string query, int top)
    {
        try
        {
            var collectionName = UserSetting.VectorCollectionName;
            return await _orchestrator.RetrieveAsync(query, collectionName, top);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "知识库搜索失败: {Query}", query);
            return new List<TextSearchResult>();
        }
    }

    /// <summary>
    /// 执行网络搜索
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteWebSearch(string query, int top)
    {
        var textSearch = _webTextSearchFactory.Create();
        if (textSearch is null)
        {
            _logger.LogWarning("网络搜索服务不可用");
            return new List<TextSearchResult>();
        }

        try
        {
            var testSearchResults = await textSearch.GetTextSearchResultsAsync(query, new TextSearchOptions
            {
                Top = top
            });
            var results = new List<TextSearchResult>();
            await foreach (var r in testSearchResults.Results)
            {
                results.Add(r);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "网络搜索失败: {Query}", query);
        }

        return new List<TextSearchResult>();
    }

    /// <summary>
    /// 合并多个搜索结果集
    /// </summary>
    private static IReadOnlyList<TextSearchResult> CombineResults(IReadOnlyList<TextSearchResult>[] results)
    {
        var combined = results.SelectMany(r => r);

        // 去重：基于链接、名称和内容
        var deduped = combined
            .GroupBy(r => $"{r.Link}|{r.Name}|{r.Value}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        return deduped;
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(SearchAsync);
    }
}