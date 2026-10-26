using MarketAssistant.Services.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Services;

/// <summary>
/// <see cref="McpToolAuditLogger"/> 记录与读取契约测试（纯内存，无 IO）。
/// </summary>
/// <remarks>
/// MCP 配置页的「工具审计」分区依赖 <c>GetRecentEntries</c> 的读取语义：
/// 按写入顺序返回、仅保留末尾 N 条、过滤事件只写日志不入账。这些约定此前无测试锁定。
/// </remarks>
[TestClass]
public sealed class McpToolAuditLoggerTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void LogToolLoaded_ThenGetRecentEntries_ShouldReturnEntryInOrder()
    {
        var logger = new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance);

        logger.LogToolLoaded("server-a", "tool-1", "行情");
        logger.LogToolLoaded("server-b", "tool-2", "交易");

        var entries = logger.GetRecentEntries();

        Assert.HasCount(2, entries);
        Assert.AreEqual("server-a", entries[0].ServerName);
        Assert.AreEqual("tool-1", entries[0].ToolName);
        Assert.AreEqual("行情", entries[0].Category);
        Assert.AreEqual(McpAuditEventType.ToolLoaded, entries[0].EventType);
        Assert.AreEqual("server-b", entries[1].ServerName);
        Assert.AreEqual(McpAuditEventType.ToolLoaded, entries[1].EventType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LogToolFiltered_ShouldNotEnterEntries_OnlyWritesLog()
    {
        // 过滤事件只写日志、不进内存账：审计账本记录的是「实际加载的工具」，
        // 避免白名单频繁调整时账本被噪声淹没
        var logger = new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance);

        logger.LogToolFiltered("server-a", "tool-x", "不在白名单");

        Assert.IsEmpty(logger.GetRecentEntries());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetRecentEntries_ShouldReturnOnlyLastN()
    {
        var logger = new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance);
        for (var i = 0; i < 5; i++)
        {
            logger.LogToolLoaded("server", $"tool-{i}", "分类");
        }

        var entries = logger.GetRecentEntries(2);

        Assert.HasCount(2, entries);
        Assert.AreEqual("tool-3", entries[0].ToolName, "应返回末尾 N 条中的较早者");
        Assert.AreEqual("tool-4", entries[1].ToolName);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetRecentEntries_OnEmptyLogger_ShouldReturnEmpty()
    {
        var logger = new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance);

        Assert.IsEmpty(logger.GetRecentEntries());
    }
}
