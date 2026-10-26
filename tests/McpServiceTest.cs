using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using TestMarketAssistant.Infrastructure;

namespace TestMarketAssistant;

/// <summary>
/// McpService 核心功能测试
/// </summary>
[TestClass]
public sealed class McpServiceTest : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [TestMethod]
    [TestCategory("Integration")]
    [Timeout(120000)]
    public async Task GetAITools_Stdio_Success()
    {
        var config = new MCPServerConfig
        {
            Name = "mcp-server-filesystem",
            TransportType = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-everything",
            // 验证工具加载能力，显式允许全部；空白名单默认不暴露由 EmptyWhitelist 测试覆盖
            AllowAllTools = true,
            EnvironmentVariables = []
        };

        var service = new McpService(
            NullLogger<McpService>.Instance,
            new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance),
            CreateConfigService());

        var tools = await service.GetAIToolsAsync([config]);

        Assert.IsNotEmpty(tools, "应该返回至少一个 AI 工具");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAITools_EmptyWhitelist_ShouldExposeNoTools()
    {
        // P1-08：空白名单且未显式 AllowAllTools 时，不暴露任何工具
        var config = new MCPServerConfig
        {
            Name = "mcp-server-filesystem",
            TransportType = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-everything",
            AllowedTools = [],
            EnvironmentVariables = []
        };

        var service = new McpService(
            NullLogger<McpService>.Instance,
            new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance),
            CreateConfigService());

        var tools = await service.GetAIToolsAsync([config]);

        Assert.HasCount(0, tools, "空白名单默认不应暴露任何 MCP 工具");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateClientTransport_InvalidType_ThrowsException()
    {
        var config = new MCPServerConfig
        {
            Name = "invalid-server",
            TransportType = "invalid-type",
            Command = "test"
        };

        Assert.ThrowsExactly<NotSupportedException>(() =>
        {
            McpService.CreateClientTransport(config);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetEnabledConfigs_ReturnsOnlyEnabled()
    {
        var configService = CreateConfigService();
        configService.AddOrUpdateConfig(new MCPServerConfig
        {
            Id = "enabled",
            Name = "enabled",
            Command = "test",
            IsEnabled = true
        });
        configService.AddOrUpdateConfig(new MCPServerConfig
        {
            Id = "disabled",
            Name = "disabled",
            Command = "test",
            IsEnabled = false
        });

        var service = new McpService(
            NullLogger<McpService>.Instance,
            new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance),
            configService);

        var configs = service.GetEnabledConfigs();

        Assert.HasCount(1, configs, "应该只返回启用的配置");
        Assert.AreEqual("enabled", configs[0].Id);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(120000)]
    public async Task GetAITools_EmptyConfigs_ReturnsEmpty()
    {
        var service = new McpService(
            NullLogger<McpService>.Instance,
            new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance),
            CreateConfigService());

        var tools = await service.GetAIToolsAsync([]);

        Assert.IsEmpty(tools, "空配置列表应该返回空工具列表");
    }

    private MCPServerConfigService CreateConfigService()
        => TestConfigServices.CreateMcpServerConfigService(_directory);
}
