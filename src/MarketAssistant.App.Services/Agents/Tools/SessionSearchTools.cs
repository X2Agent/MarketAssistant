using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;

namespace MarketAssistant.Agents.Tools;

/// <summary>
/// 历史会话搜索工具，允许 Agent 跨会话搜索过去讨论过的内容。
/// 基于 SQLite FTS5 全文索引实现。
/// </summary>
public class SessionSearchTools : IToolsProvider
{
    private readonly ChatSessionPersistenceService _persistenceService;
    private readonly ILogger<SessionSearchTools> _logger;

    public SessionSearchTools(ChatSessionPersistenceService persistenceService, ILogger<SessionSearchTools> logger)
    {
        _persistenceService = persistenceService;
        _logger = logger;
    }

    [Description("搜索历史对话记录。当用户提到过去讨论过的内容、之前的分析、或需要回忆跨会话信息时调用。" +
                 "返回匹配的对话片段、时间和标的代码。")]
    public async Task<string> SearchPastSessionsAsync(
        [Description("搜索关键词或短语")] string query,
        [Description("返回结果数量上限，默认5")] int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "请提供搜索关键词。";

        if (limit <= 0) limit = 5;
        if (limit > 15) limit = 15;

        var results = await _persistenceService.SearchSessionsAsync(query, limit);

        if (results.Count == 0)
            return $"未找到与 \"{query}\" 相关的历史对话。";

        var sb = new StringBuilder();
        sb.AppendLine($"## 历史对话搜索结果 ({results.Count} 条匹配)");
        sb.AppendLine();

        string? currentSession = null;
        foreach (var r in results)
        {
            if (currentSession != r.SessionId)
            {
                currentSession = r.SessionId;
                var stockInfo = string.IsNullOrEmpty(r.StockCode) ? "" : $" | 标的: {r.StockCode}";
                sb.AppendLine($"### {r.SessionTitle}{stockInfo} ({r.UpdatedAt:yyyy-MM-dd})");
            }

            var role = r.Role == "user" ? "用户" : (r.AuthorName ?? "助手");
            var content = r.Content.Length > 300 ? r.Content[..300] + "..." : r.Content;
            sb.AppendLine($"**{role}**: {content}");
            sb.AppendLine();
        }

        _logger.LogInformation("历史会话搜索: {Query} → {Count} 条结果", query, results.Count);
        return sb.ToString();
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(SearchPastSessionsAsync);
    }
}
