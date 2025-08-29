using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using MarketAssistant.Vectors.Services;
using MarketAssistant.Applications.Settings;

namespace MarketAssistant.Plugins;

/// <summary>
/// 智能搜索插件：根据用户设置自动选择最适合的搜索策略
/// - 仅知识库搜索：当用户启用知识库但禁用网络搜索时
/// - 仅网络搜索：当用户禁用知识库但启用网络搜索时  
/// - 混合搜索：当用户同时启用知识库和网络搜索时
/// - 无搜索：当用户都禁用时返回空结果
/// </summary>
public class GroundingSearchPlugin
{
    private readonly RetrievalOrchestrator _orchestrator;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<GroundingSearchPlugin> _logger;

    public GroundingSearchPlugin(
        RetrievalOrchestrator orchestrator,
        IUserSettingService userSettingService,
        ILogger<GroundingSearchPlugin> logger)
    {
        _orchestrator = orchestrator;
        _userSettingService = userSettingService;
        _logger = logger;
    }

    [KernelFunction, Description("智能搜索：根据用户设置自动选择知识库检索、网络搜索或混合检索，返回高质量证据片段")]
    public async Task<KernelSearchResults<TextSearchResult>> SearchAsync(
        [Description("查询问题或关键字")] string query,
        [Description("返回条数")] int top = 6)
    {
        var userSetting = _userSettingService.CurrentSetting;
        var hasKnowledgeEnabled = userSetting.LoadKnowledge;
        var hasWebSearchEnabled = userSetting.EnableWebSearch && !string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey);

        _logger.LogDebug("搜索策略 - 知识库: {KnowledgeEnabled}, 网络搜索: {WebEnabled}",
            hasKnowledgeEnabled, hasWebSearchEnabled);

        try
        {
            var searchResults = await ExecuteSearchStrategy(query, hasKnowledgeEnabled, hasWebSearchEnabled, top);
            return new KernelSearchResults<TextSearchResult>(AsAsync(searchResults));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索执行失败: {Query}", query);
            return new KernelSearchResults<TextSearchResult>(AsAsync([]));
        }
    }

    /// <summary>
    /// 根据配置执行相应的搜索策略
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteSearchStrategy(
        string query,
        bool hasKnowledgeEnabled,
        bool hasWebSearchEnabled,
        int top)
    {
        var collectionName = UserSetting.VectorCollectionName;

        return (hasKnowledgeEnabled, hasWebSearchEnabled) switch
        {
            // 混合搜索：知识库 + 网络搜索（最佳体验）
            (true, true) => await _orchestrator.RetrieveWithWebAsync(query, collectionName, top),

            // 仅知识库搜索
            (true, false) => await _orchestrator.RetrieveAsync(query, collectionName, top),

            // 仅网络搜索（需要特殊处理，因为现有方法都需要知识库）
            (false, true) => await ExecuteWebOnlySearch(query, top),

            // 无搜索：用户禁用了所有搜索功能
            (false, false) => []
        };
    }

    /// <summary>
    /// 执行仅网络搜索（当知识库被禁用时）
    /// 注意：这是一个特殊情况，需要绕过知识库直接进行网络搜索
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteWebOnlySearch(string query, int top)
    {
        _logger.LogInformation("执行仅网络搜索模式: {Query}", query);

        try
        {
            // 使用混合搜索方法，但过滤出仅网络搜索结果
            var collectionName = UserSetting.VectorCollectionName;
            var allResults = await _orchestrator.RetrieveWithWebAsync(query, collectionName, top * 2);

            // 过滤出网络搜索结果（有链接且不是本地知识库的结果）
            var webOnlyResults = allResults.Where(r =>
                !string.IsNullOrEmpty(r.Link) &&
                r.Link.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                r.Name != "未检索到相关资料")
                .Take(top)
                .ToList();

            if (webOnlyResults.Count == 0)
            {
                return new List<TextSearchResult>
                {
                    new("未检索到相关网络资料。请更换关键词或稍后重试。") { Name = "未检索到相关网络资料" }
                };
            }

            _logger.LogInformation("仅网络搜索返回 {Count} 条结果", webOnlyResults.Count);
            return webOnlyResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "仅网络搜索执行失败: {Query}", query);
            return new List<TextSearchResult>
            {
                new("网络搜索失败，请稍后重试。") { Name = "网络搜索失败" }
            };
        }
    }

    /// <summary>
    /// 转换为异步枚举（Kernel 要求的格式）
    /// </summary>
    private static async IAsyncEnumerable<TextSearchResult> AsAsync(IEnumerable<TextSearchResult> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }
}
