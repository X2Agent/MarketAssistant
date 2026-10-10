using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Mcp;

/// <summary>
/// MCP 工具调用审计日志
/// 记录所有通过 MCP 加载和调用的工具，用于安全审计和排查
/// </summary>
public class McpToolAuditLogger(ILogger<McpToolAuditLogger> logger)
{
    private readonly ILogger<McpToolAuditLogger> _logger = logger;
    private readonly List<McpToolAuditEntry> _entries = [];
    private readonly object _lock = new();

    /// <summary>
    /// 记录工具加载事件
    /// </summary>
    public void LogToolLoaded(string serverName, string toolName, string category)
    {
        var entry = new McpToolAuditEntry
        {
            EventType = McpAuditEventType.ToolLoaded,
            ServerName = serverName,
            ToolName = toolName,
            Category = category,
            Timestamp = DateTime.UtcNow
        };

        lock (_lock) { _entries.Add(entry); }
        _logger.LogInformation("[MCP 审计] 工具加载: {Server}/{Tool} (分类: {Category})",
            serverName, toolName, category);
    }

    /// <summary>
    /// 记录工具被过滤（白名单外）
    /// </summary>
    public void LogToolFiltered(string serverName, string toolName, string reason)
    {
        _logger.LogWarning("[MCP 审计] 工具被过滤: {Server}/{Tool}，原因: {Reason}",
            serverName, toolName, reason);
    }

    /// <summary>
    /// 获取最近的审计记录
    /// </summary>
    public IReadOnlyList<McpToolAuditEntry> GetRecentEntries(int count = 100)
    {
        lock (_lock)
        {
            return _entries.TakeLast(count).ToList().AsReadOnly();
        }
    }
}

public class McpToolAuditEntry
{
    public McpAuditEventType EventType { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public enum McpAuditEventType
{
    /// <summary>工具被成功加载。</summary>
    [Description("工具加载")]
    ToolLoaded,

    /// <summary>工具因白名单限制被过滤。</summary>
    [Description("白名单过滤")]
    ToolFiltered
}
