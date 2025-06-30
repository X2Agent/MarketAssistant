using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.IO.Pipelines;

namespace MarketAssistant.Plugins;

public sealed class McpPlugin
{
    public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync()
    {
        var allFunctions = new List<KernelFunction>();
        var configService = MCPServerConfigService.Instance;

        // 获取所有启用的MCP服务器配置
        var enabledConfigs = configService.ServerConfigs.Where(c => c.IsEnabled).ToList();

        foreach (var config in enabledConfigs)
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

                // 获取可用工具列表
                var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);

                // 将工具转换为KernelFunction并添加到结果列表
                var functions = tools.Select(tool => tool.AsKernelFunction()).ToList();
                allFunctions.AddRange(functions);

                Console.WriteLine($"从 {config.Name} 加载了 {functions.Count} 个工具");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接到MCP服务器 {config.Name} 时出错: {ex.Message}");
            }
        }

        return allFunctions;
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

    private static IClientTransport CreateSseTransport(MCPServerConfig config)
    {
        return new SseClientTransport(new()
        {
            Name = config.Name,
            TransportMode = HttpTransportMode.AutoDetect,
            Endpoint = new Uri(config.Command)// 对于SSE，Command字段存储URL
        });
    }

    private static IClientTransport CreateStreamableHttpTransport(MCPServerConfig config)
    {
        Pipe clientToServerPipe = new(), serverToClientPipe = new();
        return new StreamClientTransport(clientToServerPipe.Writer.AsStream(), serverToClientPipe.Reader.AsStream());
    }
}
