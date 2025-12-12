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
    [Timeout(120000)]
    public async Task GetAITools_Stdio_Success()
    {
        // Arrange
        var config = new MCPServerConfig
        {
            Name = "mcp-server-filesystem",
            TransportType = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-everything",
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var tools = await service.GetAIToolsAsync([config]);

        // Assert
        Assert.IsTrue(tools.Count > 0, "应该返回至少一个 AI 工具");
    }

    [TestMethod]
    public void CreateClientTransport_InvalidType_ThrowsException()
    {
        // Arrange
        var config = new MCPServerConfig
        {
            Name = "invalid-server",
            TransportType = "invalid-type",
            Command = "test"
        };

        // Act & Assert
        Assert.ThrowsExactly<NotSupportedException>(() =>
        {
            McpService.CreateClientTransport(config);
        });
    }

    [TestMethod]
    public void GetEnabledConfigs_ReturnsOnlyEnabled()
    {
        // Act
        var configs = McpService.GetEnabledConfigs();

        // Assert
        Assert.IsNotNull(configs);
        Assert.IsTrue(configs.All(c => c.IsEnabled), "应该只返回启用的配置");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task GetAITools_WithLifetimeManagement_Success()
    {
        // Arrange
        var config = new MCPServerConfig
        {
            Name = "mcp-server-test",
            TransportType = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-everything",
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        await using var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var tools = await service.GetAIToolsAsync([config]);

        // Assert
        Assert.IsTrue(tools.Count > 0);

        // Cleanup - service 会自动释放客户端
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task GetAITools_EmptyConfigs_ReturnsEmpty()
    {
        // Arrange
        var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var tools = await service.GetAIToolsAsync([]);

        // Assert
        Assert.AreEqual(0, tools.Count, "空配置列表应该返回空工具列表");
    }
}

