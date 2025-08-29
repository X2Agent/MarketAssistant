using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using MarketAssistant.Vectors.Services;
using MarketAssistant.Applications.Settings;

namespace MarketAssistant.Plugins;

/// <summary>
/// ������������������û������Զ�ѡ�����ʺϵ���������
/// - ��֪ʶ�����������û�����֪ʶ�⵫������������ʱ
/// - ���������������û�����֪ʶ�⵫������������ʱ  
/// - ������������û�ͬʱ����֪ʶ�����������ʱ
/// - �����������û�������ʱ���ؿս��
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

    [KernelFunction, Description("���������������û������Զ�ѡ��֪ʶ������������������ϼ��������ظ�����֤��Ƭ��")]
    public async Task<KernelSearchResults<TextSearchResult>> SearchAsync(
        [Description("��ѯ�����ؼ���")] string query,
        [Description("��������")] int top = 6)
    {
        var userSetting = _userSettingService.CurrentSetting;
        var hasKnowledgeEnabled = userSetting.LoadKnowledge;
        var hasWebSearchEnabled = userSetting.EnableWebSearch && !string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey);

        _logger.LogDebug("�������� - ֪ʶ��: {KnowledgeEnabled}, ��������: {WebEnabled}",
            hasKnowledgeEnabled, hasWebSearchEnabled);

        try
        {
            var searchResults = await ExecuteSearchStrategy(query, hasKnowledgeEnabled, hasWebSearchEnabled, top);
            return new KernelSearchResults<TextSearchResult>(AsAsync(searchResults));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "����ִ��ʧ��: {Query}", query);
            return new KernelSearchResults<TextSearchResult>(AsAsync([]));
        }
    }

    /// <summary>
    /// ��������ִ����Ӧ����������
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
            // ���������֪ʶ�� + ����������������飩
            (true, true) => await _orchestrator.RetrieveWithWebAsync(query, collectionName, top),

            // ��֪ʶ������
            (true, false) => await _orchestrator.RetrieveAsync(query, collectionName, top),

            // ��������������Ҫ���⴦����Ϊ���з�������Ҫ֪ʶ�⣩
            (false, true) => await ExecuteWebOnlySearch(query, top),

            // ���������û�������������������
            (false, false) => []
        };
    }

    /// <summary>
    /// ִ�н�������������֪ʶ�ⱻ����ʱ��
    /// ע�⣺����һ�������������Ҫ�ƹ�֪ʶ��ֱ�ӽ�����������
    /// </summary>
    private async Task<IReadOnlyList<TextSearchResult>> ExecuteWebOnlySearch(string query, int top)
    {
        _logger.LogInformation("ִ�н���������ģʽ: {Query}", query);

        try
        {
            // ʹ�û�����������������˳��������������
            var collectionName = UserSetting.VectorCollectionName;
            var allResults = await _orchestrator.RetrieveWithWebAsync(query, collectionName, top * 2);

            // ���˳���������������������Ҳ��Ǳ���֪ʶ��Ľ����
            var webOnlyResults = allResults.Where(r =>
                !string.IsNullOrEmpty(r.Link) &&
                r.Link.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                r.Name != "δ�������������")
                .Take(top)
                .ToList();

            if (webOnlyResults.Count == 0)
            {
                return new List<TextSearchResult>
                {
                    new("δ����������������ϡ�������ؼ��ʻ��Ժ����ԡ�") { Name = "δ�����������������" }
                };
            }

            _logger.LogInformation("�������������� {Count} �����", webOnlyResults.Count);
            return webOnlyResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "����������ִ��ʧ��: {Query}", query);
            return new List<TextSearchResult>
            {
                new("��������ʧ�ܣ����Ժ����ԡ�") { Name = "��������ʧ��" }
            };
        }
    }

    /// <summary>
    /// ת��Ϊ�첽ö�٣�Kernel Ҫ��ĸ�ʽ��
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
