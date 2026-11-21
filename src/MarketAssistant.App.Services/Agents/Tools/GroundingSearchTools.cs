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
public class GroundingSearchTools(
    IRetrievalOrchestrator orchestrator,
    IWebSearchService webSearchService,
    IUserSettingService userSettingService,
    ILogger<GroundingSearchTools> logger) : IToolsProvider
{
    private readonly IRetrievalOrchestrator _orchestrator = orchestrator;
    private readonly IWebSearchService _webSearchService = webSearchService;
    private readonly IUserSettingService _userSettingService = userSettingService;
    private readonly ILogger<GroundingSearchTools> _logger = logger;

    [Description("综合信息检索工具。可同时检索互联网公开信息和内部知识库（如用户文档、历史研报）。")]
    public Task<string> SearchAsync(
        [Description("搜索的查询语句或关键词。")] string query,
        [Description("返回结果数量，建议3-6个")] int top = 6,
        CancellationToken cancellationToken = default)
    {
        // 参数约束：避免极端模式使用和极端参数
        if (top <= 0) top = 3;
        if (top > 6) top = 6;

        return ToolExecutor.ExecuteAsync(
            operationName: "综合信息检索",
            assetSymbol: query,
            logger: _logger,
            action: async ct =>
            {
                var userSetting = _userSettingService.CurrentSetting;
                var hasKnowledgeEnabled = userSetting.LoadKnowledge;
                var hasWebSearchEnabled = userSetting.EnableWebSearch && !string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey);

                var searchResults = await ExecuteSearchStrategy(query, hasKnowledgeEnabled, hasWebSearchEnabled, top, ct);
                return FormatResults(searchResults.Take(top));
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 渲染检索结果并包进外部数据围栏：网页与用户文档内容可能夹带指令，不能让模型当作指令执行。
    /// </summary>
    private static string FormatResults(IEnumerable<TextSearchResult> results)
    {
        var items = results
            .Select((result, index) => $"[{index + 1}] {result.Name}\n来源: {result.Link}\n{result.Value}")
            .ToList();

        return items.Count == 0
            ? "未检索到相关结果"
            : ExternalDataFence.Wrap(string.Join("\n\n", items), "待分析的外部资料");
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

        // 并行执行启用的搜索方式
        var tasks = new List<Task<IReadOnlyList<TextSearchResult>>>();

        if (hasKnowledgeEnabled)
        {
            tasks.Add(_orchestrator.RetrieveAsync(query, UserSetting.VectorCollectionName, top, cancellationToken));
        }

        if (hasWebSearchEnabled)
        {
            tasks.Add(_webSearchService.SearchAsync(query, top, cancellationToken));
        }

        // 未启用任何搜索方式：空结果属正常配置状态
        if (tasks.Count == 0)
        {
            return [];
        }

        // 仅启用单路：失败直接上抛（由 ToolExecutor 转 FriendlyException），
        // 调用方感知「检索失败」而非「无结果」
        if (tasks.Count == 1)
        {
            return await tasks[0];
        }

        // 多路混用：逐路隔离，单路失败不拖垮另一路
        var isolated = await Task.WhenAll(tasks.Select(IsolateFailureAsync));
        return CombineResults(isolated);
    }

    /// <summary>多路混用时隔离单路失败：失败路记警告并返回空，另一路结果仍可用。</summary>
    private async Task<IReadOnlyList<TextSearchResult>> IsolateFailureAsync(Task<IReadOnlyList<TextSearchResult>> task)
    {
        try
        {
            return await task;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "混合搜索中单路失败，忽略该路结果");
            return [];
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