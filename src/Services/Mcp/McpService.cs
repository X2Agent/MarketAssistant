using MarketAssistant.Applications.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;

namespace MarketAssistant.Services.Mcp;

/// <summary>
/// MCP（Model Context Protocol）服务
/// 统一处理 MCP 客户端的创建、连接和工具加载
/// </summary>
public class McpService : IAsyncDisposable
{
    private readonly ILogger<McpService>? _logger;
    private readonly List<McpClient> _mcpClients = new();
    private bool _disposed;

    /// <summary>
    /// 创建 MCP 服务
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public McpService(ILogger<McpService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取 MCP 工具作为 AITool 列表（用于新的 Agent Framework）
    /// </summary>
    /// <param name="configs">MCP 服务器配置列表</param>
    /// <param name="manageClientLifetime">是否管理客户端生命周期</param>
    /// <returns>AITool 列表</returns>
    public async Task<List<AITool>> GetAIToolsAsync(
        IEnumerable<MCPServerConfig> configs,
        bool manageClientLifetime = true)
    {
        var tools = new List<AITool>();

        foreach (var config in configs)
        {
            try
            {
                var clientTransport = CreateClientTransport(config);
                var options = new McpClientOptions
                {
                    ClientInfo = new() { Name = config.Name, Version = "1.0.0" }
                };

                var mcpClient = await McpClient.CreateAsync(clientTransport, options);

                if (manageClientLifetime)
                {
                    _mcpClients.Add(mcpClient);
                }

                var mcpTools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
                tools.AddRange(mcpTools.Cast<AITool>());

                _logger?.LogInformation("成功连接到 MCP 服务器 {Name}，加载 {Count} 个工具",
                    config.Name, mcpTools.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "连接到 MCP 服务器 {Name} 失败", config.Name);
            }
        }

        return tools;
    }

    /// <summary>
    /// 获取 MCP 工具作为 KernelFunction 列表（用于 Semantic Kernel）
    /// </summary>
    /// <param name="configs">MCP 服务器配置列表</param>
    /// <returns>KernelFunction 列表</returns>
    public async Task<List<KernelFunction>> GetKernelFunctionsAsync(
        IEnumerable<MCPServerConfig> configs)
    {
        var kernelFunctions = new List<KernelFunction>();

        foreach (var config in configs)
        {
            try
            {
                var clientTransport = CreateClientTransport(config);
                var options = new McpClientOptions
                {
                    ClientInfo = new() { Name = config.Name, Version = "1.0.0" }
                };

                // 对于 KernelFunction，使用 using 模式自动释放客户端
                await using var mcpClient = await McpClient.CreateAsync(clientTransport, options);

                var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
                kernelFunctions.AddRange(tools.Select(aiFunction => aiFunction.AsKernelFunction()));

                _logger?.LogInformation("成功连接到 MCP 服务器 {Name}，加载 {Count} 个 Kernel 函数",
                    config.Name, tools.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "连接到 MCP 服务器 {Name} 失败", config.Name);
            }
        }

        return kernelFunctions;
    }

    /// <summary>
    /// 获取所有启用的 MCP 服务器配置
    /// </summary>
    /// <returns>启用的配置列表</returns>
    public static List<MCPServerConfig> GetEnabledConfigs()
    {
        var configService = MCPServerConfigService.Instance;
        return configService.ServerConfigs.Where(c => c.IsEnabled).ToList();
    }

    /// <summary>
    /// 创建客户端传输
    /// </summary>
    /// <param name="config">MCP 服务器配置</param>
    /// <returns>客户端传输实例</returns>
    /// <exception cref="NotSupportedException">不支持的传输类型</exception>
    public static IClientTransport CreateClientTransport(MCPServerConfig config)
    {
        return config.TransportType.ToLower() switch
        {
            "stdio" => CreateStdioTransport(config),
            "sse" => CreateSseTransport(config),
            "streamablehttp" => CreateStreamableHttpTransport(config),
            _ => throw new NotSupportedException($"不支持的传输类型: {config.TransportType}")
        };
    }

    /// <summary>
    /// 创建 Stdio 传输
    /// </summary>
    private static IClientTransport CreateStdioTransport(MCPServerConfig config)
    {
        var arguments = string.IsNullOrEmpty(config.Arguments)
            ? Array.Empty<string>()
            : config.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new StdioClientTransport(new()
        {
            Name = config.Name,
            Command = config.Command,
            Arguments = arguments,
            EnvironmentVariables = config.EnvironmentVariables
        });
    }

    /// <summary>
    /// 创建 SSE 传输
    /// </summary>
    private static IClientTransport CreateSseTransport(MCPServerConfig config)
    {
        return new HttpClientTransport(new()
        {
            Name = config.Name,
            TransportMode = HttpTransportMode.AutoDetect,
            Endpoint = new Uri(config.Command)
        });
    }

    /// <summary>
    /// 创建 Streamable HTTP 传输
    /// </summary>
    private static IClientTransport CreateStreamableHttpTransport(MCPServerConfig config)
    {
        return new HttpClientTransport(new()
        {
            Name = config.Name,
            TransportMode = HttpTransportMode.StreamableHttp,
            Endpoint = new Uri(config.Command)
        });
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        foreach (var mcpClient in _mcpClients)
        {
            try
            {
                await mcpClient.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "释放 MCP 客户端时发生错误");
            }
        }

        _mcpClients.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

