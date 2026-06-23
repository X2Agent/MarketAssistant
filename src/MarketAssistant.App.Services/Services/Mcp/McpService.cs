using MarketAssistant.Applications.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MarketAssistant.Services.Mcp;

/// <summary>
/// MCP（Model Context Protocol）服务
/// 统一处理 MCP 客户端的创建、连接和工具加载
/// </summary>
public class McpService : IAsyncDisposable
{
    private readonly ILogger<McpService> _logger;
    private readonly McpToolAuditLogger _auditLogger;
    private readonly MCPServerConfigService _configService;
    private readonly List<McpClient> _mcpClients = new();
    private readonly object _clientsLock = new();
    private bool _disposed;

    /// <summary>
    /// 已连接的 MCP 服务器数量
    /// </summary>
    public int ActiveConnectionCount
    {
        get { lock (_clientsLock) return _mcpClients.Count; }
    }

    /// <summary>
    /// 创建 MCP 服务
    /// </summary>
    public McpService(
        ILogger<McpService> logger,
        McpToolAuditLogger auditLogger,
        MCPServerConfigService configService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    /// <summary>
    /// 获取 MCP 工具作为 AITool 列表
    /// </summary>
    /// <param name="configs">MCP 服务器配置列表</param>
    /// <returns>AITool 列表</returns>
    public async Task<List<AITool>> GetAIToolsAsync(
        IEnumerable<MCPServerConfig> configs)
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

                lock (_clientsLock)
                {
                    _mcpClients.Add(mcpClient);
                }

                var mcpTools = await mcpClient.ListToolsAsync().ConfigureAwait(false);

                foreach (var tool in mcpTools.Cast<AITool>())
                {
                    var toolName = tool.Name;

                    if (config.AllowedTools.Count > 0 &&
                        !config.AllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
                    {
                        _auditLogger.LogToolFiltered(config.Name, toolName,
                            "不在允许列表中");
                        continue;
                    }

                    _auditLogger.LogToolLoaded(config.Name, toolName, config.Category);
                    tools.Add(tool);
                }

                _logger.LogInformation(
                    "成功连接到 MCP 服务器 {Name} (分类: {Category})，加载 {Count}/{Total} 个工具",
                    config.Name, config.Category, tools.Count, mcpTools.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "连接到 MCP 服务器 {Name} 失败", config.Name);
            }
        }

        return tools;
    }

    /// <summary>
    /// 获取所有启用的 MCP 服务器配置
    /// </summary>
    /// <returns>启用的配置列表</returns>
    public List<MCPServerConfig> GetEnabledConfigs()
    {
        return _configService.ServerConfigs.Where(c => c.IsEnabled).ToList();
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
    /// 断开所有现有 MCP 连接，释放资源。
    /// 用于配置变更后重建连接。
    /// </summary>
    public async Task ResetConnectionsAsync()
    {
        List<McpClient> clientsToDispose;
        lock (_clientsLock)
        {
            clientsToDispose = [.. _mcpClients];
            _mcpClients.Clear();
        }

        foreach (var client in clientsToDispose)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重置 MCP 连接时释放客户端出错");
            }
        }

        _logger.LogInformation("已重置 {Count} 个 MCP 连接", clientsToDispose.Count);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        List<McpClient> clientsToDispose;
        lock (_clientsLock)
        {
            clientsToDispose = [.. _mcpClients];
            _mcpClients.Clear();
        }

        foreach (var mcpClient in clientsToDispose)
        {
            try
            {
                await mcpClient.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放 MCP 客户端时发生错误");
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

