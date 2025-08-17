using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;

namespace MarketAssistant.Plugins;

public sealed class McpPlugin
{
    public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync()
    {
        var configService = MCPServerConfigService.Instance;
        var enabledConfigs = configService.ServerConfigs.Where(c => c.IsEnabled).ToList();
        return await GetKernelFunctionsAsync(enabledConfigs).ConfigureAwait(false);
    }

    public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync(MCPServerConfig mCPServerConfig)
    {
        return await GetKernelFunctionsAsync([mCPServerConfig]).ConfigureAwait(false);
    }

    public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync(
        IEnumerable<MCPServerConfig> configs)
    {
        var kernelFuns = new List<KernelFunction>();

        foreach (var config in configs)
        {
            try
            {
                IClientTransport clientTransport;

                // 根据传输类型创建相应的客户端传输
                switch (config.TransportType.ToLower())
                {
                    case "stdio":
                        clientTransport = CreateStdioTransport(config);
                        break;
                    case "sse":
                        clientTransport = CreateSseTransport(config);
                        break;
                    case "streamablehttp":
                        clientTransport = CreateStreamableHttpTransport(config);
                        break;
                    default:
                        Console.WriteLine($"不支持的传输类型: {config.TransportType}");
                        continue;
                }

                var options = new McpClientOptions
                {
                    ClientInfo = new() { Name = $"{config.Name}", Version = "1.0.0" }
                };

                // 创建MCP客户端
                await using var mcpClient = await McpClientFactory.CreateAsync(clientTransport, options);

                // 获取可用工具列表（暂不映射为 SK 函数，仅验证可用性）
                var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
                Console.WriteLine($"从 {config.Name} 加载了 {tools.Count} 个工具");

                kernelFuns.AddRange(tools.Select(aiFunction => aiFunction.AsKernelFunction()));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接到MCP服务器 {config.Name} 时出错: {ex.Message}");
            }
        }

        return kernelFuns;
    }

    private static IClientTransport CreateStdioTransport(MCPServerConfig config)
    {
        var arguments = string.IsNullOrEmpty(config.Arguments)
            ? new string[0]
            : config.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new StdioClientTransport(new()
        {
            Name = config.Name,
            Command = config.Command,
            Arguments = arguments,
            EnvironmentVariables = config.EnvironmentVariables
        });
    }

    internal static IClientTransport CreateSseTransport(MCPServerConfig config)
    {
        // 明确以 SSE 模式连接
        return new SseClientTransport(new()
        {
            Name = config.Name,
            TransportMode = HttpTransportMode.AutoDetect,
            Endpoint = new Uri(config.Command) // 对于SSE，Command字段存储URL
        });
    }

    internal static IClientTransport CreateStreamableHttpTransport(MCPServerConfig config)
    {
        // 使用 Streamable HTTP 模式（同一实现通过传输模式区分）
        return new SseClientTransport(new()
        {
            Name = config.Name,
            TransportMode = HttpTransportMode.StreamableHttp,
            Endpoint = new Uri(config.Command) // 对于StreamableHttp，Command字段存储URL
        });
    }
}
