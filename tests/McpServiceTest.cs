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
    public async Task GetKernelFunctions_Stdio_Success()
    {
        // Arrange - 使用官方无令牌 stdio MCP 服务
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
        var functions = await service.GetKernelFunctionsAsync([config]);

        // Assert
        Assert.IsTrue(functions.Count > 0, "应该返回至少一个 Kernel 函数");
    }

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
        var tools = await service.GetAIToolsAsync([config], manageClientLifetime: false);

        // Assert
        Assert.IsTrue(tools.Count > 0, "应该返回至少一个 AI 工具");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task GetKernelFunctions_Sse_Success()
    {
        // Arrange
        var url = Environment.GetEnvironmentVariable("MCP_SSE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Inconclusive("Set MCP_SSE_URL to run this test.");
            return;
        }

        var config = new MCPServerConfig
        {
            Name = "sse-server",
            TransportType = "sse",
            Command = url
        };

        var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var functions = await service.GetKernelFunctionsAsync([config]);

        // Assert
        Assert.IsTrue(functions.Count > 0);
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task GetKernelFunctions_StreamableHttp_Success()
    {
        // Arrange
        var url = Environment.GetEnvironmentVariable("MCP_STREAM_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Inconclusive("Set MCP_STREAM_URL to run this test.");
            return;
        }

        var config = new MCPServerConfig
        {
            Name = "stream-server",
            TransportType = "streamableHttp",
            Command = url
        };

        var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var functions = await service.GetKernelFunctionsAsync([config]);

        // Assert
        Assert.IsTrue(functions.Count > 0);
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
        Assert.ThrowsException<NotSupportedException>(() =>
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
        var tools = await service.GetAIToolsAsync([config], manageClientLifetime: true);

        // Assert
        Assert.IsTrue(tools.Count > 0);

        // Cleanup - service 会自动释放客户端
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task GetKernelFunctions_MultipleConfigs_Success()
    {
        // Arrange
        var configs = new List<MCPServerConfig>
        {
            new()
            {
                Name = "server1",
                TransportType = "stdio",
                Command = "npx",
                Arguments = "-y @modelcontextprotocol/server-everything",
                EnvironmentVariables = new Dictionary<string, string?>()
            }
        };

        var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var functions = await service.GetKernelFunctionsAsync(configs);

        // Assert
        Assert.IsTrue(functions.Count > 0);
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task GetKernelFunctions_EmptyConfigs_ReturnsEmpty()
    {
        // Arrange
        var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var functions = await service.GetKernelFunctionsAsync([]);

        // Assert
        Assert.AreEqual(0, functions.Count, "空配置列表应该返回空函数列表");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task GetAITools_EmptyConfigs_ReturnsEmpty()
    {
        // Arrange
        var service = new McpService(NullLogger<McpService>.Instance);

        // Act
        var tools = await service.GetAIToolsAsync([], manageClientLifetime: false);

        // Assert
        Assert.AreEqual(0, tools.Count, "空配置列表应该返回空工具列表");
    }
}

