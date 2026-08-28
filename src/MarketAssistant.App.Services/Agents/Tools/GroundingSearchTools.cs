using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Data;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools;

/// <summary>
/// 智能搜索插件，根据用户设置自动选择最适合的搜索策略：
/// - 仅知识库：当用户启用知识库但禁用网络搜索时
/// - 仅网络搜索：当用户禁用知识库但启用网络搜索时  
/// - 混合搜索：当用户同时启用知识库和网络搜索时
/// - 空结果：当用户都未启用时，返回空结果
/// </summary>
public class GroundingSearchTools : IToolsProvider
{
    private readonly IRetrievalOrchestrator _orchestrator;
    private readonly IWebSearchService _webSearchService;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<GroundingSearchTools> _logger;

    public GroundingSearchTools(
        IRetrievalOrchestrator orchestrator,
        IWebSearchService webSearchService,
        IUserSettingService userSettingService,
        ILogger<GroundingSearchTools> logger)
    {
        _orchestrator = orchestrator;
        _webSearchService = webSearchService;
        _userSettingService = userSettingService;
        _logger = logger;
    }

    [Description("综合信息检索工具。可同时检索互联网公开信息和内部知识库（如用户文档、历史研报）。")]
    public async Task<List<TextSearchResult>> SearchAsync(
        [Description("搜索的查询语句或关键词。")] string query,
        [Description("返回结果数量，建议3-6个")] int top = 6,
        CancellationToken cancellationToken = default)
    {
        // 参数约束：避免极端模式使用和极端参数
        if (top <= 0) top = 3;
        if (top > 6) top = 6;

        _logger.LogDebug("开始搜索 - 查询: {Query}, 数量: {Top}", query, top);

        var userSetting = _userSettingService.CurrentSetting;
        var hasKnowledgeEnabled = userSetting.LoadKnowledge;
        var hasWebSearchEnabled = userSetting.EnableWebSearch && !string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey);

        _logger.LogDebug("搜索配置 - 知识库: {KnowledgeEnabled}, 网络搜索: {WebEnabled}",
            hasKnowledgeEnabled, hasWebSearchEnabled);

        try
        {
            var searchResults = await ExecuteSearchStrategy(query, hasKnowledgeEnabled, hasWebSearchEnabled, top, cancellationToken);
            return searchResults.Take(top).ToList();
        }
        catch (OperationCanceledException)
        {
            // 取消必须向上传播，不得吞成空结果
            throw;
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
        int top,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("执行搜索策略 - 知识库: {Knowledge}, 网络: {Web}, 查询: {Query}",
            hasKnowledgeEnabled, hasWebSearchEnabled, query);

        // 并行执行启用的搜索方式（各路搜索内部已做失败隔离，不会互相拖垮）
        var tasks = new List<Task<IReadOnlyList<TextSearchResult>>>();

        if (hasKnowledgeEnabled)
        {
            tasks.Add(ExecuteKnowledgeSearch(query, top, cancellationToken));
        }

        if (hasWebSearchEnabled)
        {
            tasks.Add(ExecuteWebSearch(query, top, cancellationToken));
        }

        // 如果没有启用任何搜索方式
        if (tasks.Count == 0)
        {
            return [];
        }

        // 等待所有任务完成并合并结果
        var results = await Task.WhenAll(tasks);
        return CombineResults(results);
    }

    /// <summary>
    /// 执行知识库搜索
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteKnowledgeSearch(string query, int top, CancellationToken cancellationToken)
    {
        try
        {
            var collectionName = UserSetting.VectorCollectionName;
            return await _orchestrator.RetrieveAsync(query, collectionName, top, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "知识库搜索失败: {Query}", query);
            return new List<TextSearchResult>();
        }
    }

    /// <summary>
    /// 执行网络搜索。失败时仅记录告警并返回空结果，
    /// 不让网络搜索异常拖垮已经成功的知识库结果。
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteWebSearch(string query, int top, CancellationToken cancellationToken)
    {
        try
        {
            return await _webSearchService.SearchAsync(query, top, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "网络搜索失败，忽略网络结果: {Query}", query);
            return new List<TextSearchResult>();
        }
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