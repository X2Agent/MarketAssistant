using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant;

/// <summary>
/// McpService 核心功能测试
/// </summary>
[TestClass]
public class McpServiceTest
{
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
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var service = new McpService(NullLogger<McpService>.Instance);

        var tools = await service.GetAIToolsAsync([config]);

        Assert.IsTrue(tools.Count > 0, "应该返回至少一个 AI 工具");
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
        var configService = new MCPServerConfigService();
        configService.ServerConfigs.Clear();
        configService.ServerConfigs.AddRange(
        [
            new MCPServerConfig { Name = "enabled", Command = "test", IsEnabled = true },
            new MCPServerConfig { Name = "disabled", Command = "test", IsEnabled = false }
        ]);

        var service = new McpService(NullLogger<McpService>.Instance, null, configService);

        var configs = service.GetEnabledConfigs();

        Assert.IsNotNull(configs);
        Assert.IsTrue(configs.All(c => c.IsEnabled), "应该只返回启用的配置");
    }

    [TestMethod]
    [TestCategory("Integration")]
    [Timeout(120000)]
    public async Task GetAITools_WithLifetimeManagement_Success()
    {
        var config = new MCPServerConfig
        {
            Name = "mcp-server-test",
            TransportType = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-everything",
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        await using var service = new McpService(NullLogger<McpService>.Instance);

        var tools = await service.GetAIToolsAsync([config]);

        Assert.IsTrue(tools.Count > 0);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(120000)]
    public async Task GetAITools_EmptyConfigs_ReturnsEmpty()
    {
        var service = new McpService(NullLogger<McpService>.Instance);

        var tools = await service.GetAIToolsAsync([]);

        Assert.AreEqual(0, tools.Count, "空配置列表应该返回空工具列表");
    }
}
