using MarketAssistant.Applications.Settings;
using MarketAssistant.Plugins;

namespace TestMarketAssistant;

[TestClass]
public class McpPluginTests
{
    [TestMethod]
    [Timeout(120000)]
    public async Task MCP_Stdio_ListTools()
    {
        // 使用官方无令牌 stdio MCP 服务：@modelcontextprotocol/server-filesystem
        var cfg = new MCPServerConfig
        {
            Name = "mcp-server-filesystem",
            TransportType = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-everything",
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var functions = await McpPlugin.GetKernelFunctionsAsync([cfg]);
        Assert.IsTrue(functions.Count() > 0);
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MCP_Sse_ListTools()
    {
        var url = Environment.GetEnvironmentVariable("MCP_SSE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Inconclusive("Set MCP_SSE_URL to run this test.");
            return;
        }

        var cfg = new MCPServerConfig
        {
            Name = "sse-real",
            TransportType = "sse",
            Command = url
        };

        var functions = await McpPlugin.GetKernelFunctionsAsync([cfg]);
        Assert.IsTrue(functions.Count() > 0);
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MCP_StreamableHttp_ListTools()
    {
        var url = Environment.GetEnvironmentVariable("MCP_STREAM_URL");

        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Inconclusive("Set MCP_STREAM_URL to run this test.");
            return;
        }

        var cfg = new MCPServerConfig
        {
            Name = "stream-real",
            TransportType = "streamableHttp",
            Command = url
        };

        var functions = await McpPlugin.GetKernelFunctionsAsync([cfg]);
        Assert.IsTrue(functions.Count() > 0);
    }
}


