using System.Security.Cryptography;
using System.Text;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MarketAssistant.Services.Mcp;

/// <summary>
/// MCP（Model Context Protocol）服务。
/// 按连接配置指纹复用客户端，并保留已向 Agent 暴露过工具的旧客户端，直至应用退出。
/// </summary>
public sealed class McpService : IAsyncDisposable
{
    private const int MaxRetainedRuntimes = 32;

    private readonly ILogger<McpService> _logger;
    private readonly McpToolAuditLogger _auditLogger;
    private readonly MCPServerConfigService _configService;
    private readonly IMcpClientSessionFactory _clientFactory;
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly Dictionary<string, McpRuntime> _activeRuntimes = new(StringComparer.Ordinal);
    private readonly List<McpRuntime> _retainedRuntimes = [];

    /// <summary>活动 MCP 连接计数。与 _activeRuntimes 同步维护，供属性无锁读取，避免 UI 线程同步等待信号量。</summary>
    private int _activeRuntimeCount;

    private bool _disposed;

    /// <summary>
    /// 当前活动配置对应的 MCP 连接数量。
    /// 配置刷新后，旧连接会保留到服务释放，但不计入活动连接。
    /// </summary>
    public int ActiveConnectionCount
    {
        get => Volatile.Read(ref _activeRuntimeCount);
    }

    public McpService(
        ILogger<McpService> logger,
        McpToolAuditLogger auditLogger,
        MCPServerConfigService configService)
        : this(logger, auditLogger, configService, new McpClientSessionFactory())
    {
    }

    internal McpService(
        ILogger<McpService> logger,
        McpToolAuditLogger auditLogger,
        MCPServerConfigService configService,
        IMcpClientSessionFactory clientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <summary>
    /// 获取 MCP 工具作为 AITool 列表。
    /// 相同连接配置复用已建立的客户端；工具白名单在每次读取时重新应用。
    /// </summary>
    public async Task<List<AITool>> GetAIToolsAsync(
        IEnumerable<MCPServerConfig> configs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var tools = new List<AITool>();
        foreach (var config in configs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var activity = MarketAssistantDiagnostics.StartActivity("mcp.tools.load");
            activity?.SetTag("server.address", config.Name);
            activity?.SetTag("network.transport", config.TransportType);
            activity?.SetTag("marketassistant.mcp.category", config.Category);

            try
            {
                var runtime = await GetOrCreateRuntimeAsync(config, cancellationToken)
                    .ConfigureAwait(false);
                var loadedForServer = AddAllowedTools(config, runtime.Tools, tools);
                activity?.SetTag("marketassistant.mcp.tools.available_count", runtime.Tools.Count);
                activity?.SetTag("marketassistant.mcp.tools.loaded_count", loadedForServer);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);

                _logger.LogInformation(
                    "成功连接到 MCP 服务器 {Name} (分类: {Category})，加载 {LoadedCount}/{TotalCount} 个工具",
                    config.Name,
                    config.Category,
                    loadedForServer,
                    runtime.Tools.Count);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "cancelled");
                activity?.SetTag("error.type", ex.GetType().FullName);
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                MarketAssistantDiagnostics.RecordException(activity, ex);
                throw;
            }
            catch (Exception ex)
            {
                MarketAssistantDiagnostics.RecordException(activity, ex);
                _logger.LogWarning(ex, "连接到 MCP 服务器 {Name} 失败", config.Name);
            }
        }

        return tools;
    }

    /// <summary>
    /// 获取所有启用的 MCP 服务器配置。
    /// </summary>
    public List<MCPServerConfig> GetEnabledConfigs()
    {
        return _configService.ServerConfigs.Where(config => config.IsEnabled).ToList();
    }

    /// <summary>
    /// 枚举指定服务器提供的全部工具（名称与描述），供配置页勾选工具白名单使用。
    /// 相同连接配置复用已建立的客户端。
    /// </summary>
    public async Task<IReadOnlyList<(string Name, string Description)>> GetServerToolsAsync(
        MCPServerConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var runtime = await GetOrCreateRuntimeAsync(config, cancellationToken)
            .ConfigureAwait(false);
        return [.. runtime.Tools.Select(tool => (tool.Name, tool.Description))];
    }

    /// <summary>
    /// 创建客户端传输。
    /// </summary>
    public static IClientTransport CreateClientTransport(MCPServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.TransportType.ToLowerInvariant() switch
        {
            "stdio" => CreateStdioTransport(config),
            "sse" => CreateSseTransport(config),
            "streamablehttp" => CreateStreamableHttpTransport(config),
            _ => throw new NotSupportedException($"不支持的传输类型: {config.TransportType}")
        };
    }

    /// <summary>
    /// 使当前活动连接映射失效。
    /// 已向 Agent 暴露的工具持有底层客户端引用，因此旧客户端不能在刷新时立即释放。
    /// </summary>
    public async Task ResetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        await _runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var invalidatedCount = Interlocked.Exchange(ref _activeRuntimeCount, 0);
            _activeRuntimes.Clear();

            _logger.LogInformation(
                "已使 {Count} 个 MCP 活动连接失效；旧连接将保留到应用退出，以保证已注入工具仍可调用",
                invalidatedCount);
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<McpRuntime> runtimesToDispose;

        await _runtimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            runtimesToDispose = [.. _retainedRuntimes];
            _activeRuntimes.Clear();
            _retainedRuntimes.Clear();
            Interlocked.Exchange(ref _activeRuntimeCount, 0);
        }
        finally
        {
            _runtimeGate.Release();
        }

        foreach (var runtime in runtimesToDispose)
        {
            try
            {
                await runtime.Session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放 MCP 客户端时发生错误: {Name}", runtime.ServerName);
            }
        }

        _runtimeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static string ComputeConfigurationFingerprint(MCPServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var canonical = new StringBuilder()
            .Append(config.Name.Trim()).Append('\n')
            .Append(config.TransportType.Trim().ToLowerInvariant()).Append('\n')
            .Append(config.Command.Trim()).Append('\n')
            .Append(config.Arguments.Trim()).Append('\n');

        foreach (var variable in config.EnvironmentVariables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            canonical
                .Append(variable.Key)
                .Append('=')
                .Append(variable.Value)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private async Task<McpRuntime> GetOrCreateRuntimeAsync(
        MCPServerConfig config,
        CancellationToken cancellationToken)
    {
        var fingerprint = ComputeConfigurationFingerprint(config);

        await _runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_activeRuntimes.TryGetValue(fingerprint, out var existingRuntime))
                return existingRuntime;

            if (_retainedRuntimes.Count >= MaxRetainedRuntimes)
            {
                throw new InvalidOperationException(
                    $"MCP Runtime 已达到安全上限 {MaxRetainedRuntimes}。" +
                    "为避免释放仍被 Agent 工具引用的客户端，本次连接被拒绝；请重启应用后重试。");
            }

            var runtime = await CreateRuntimeAsync(config, fingerprint, cancellationToken)
                .ConfigureAwait(false);
            _activeRuntimes.Add(fingerprint, runtime);
            _retainedRuntimes.Add(runtime);
            Interlocked.Increment(ref _activeRuntimeCount);
            return runtime;
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    private async Task<McpRuntime> CreateRuntimeAsync(
        MCPServerConfig config,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        IMcpClientSession? session = null;
        try
        {
            session = await _clientFactory.CreateAsync(config, cancellationToken).ConfigureAwait(false);
            var runtimeTools = await session.ListToolsAsync(cancellationToken).ConfigureAwait(false);
            return new McpRuntime(config.Name, fingerprint, session, runtimeTools);
        }
        catch
        {
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private int AddAllowedTools(
        MCPServerConfig config,
        IReadOnlyList<AITool> availableTools,
        List<AITool> destination)
    {
        // 最小暴露原则：空白名单默认不暴露任何工具，只有显式 AllowAllTools 才放行全部
        if (!config.AllowAllTools && config.AllowedTools.Count == 0)
        {
            _logger.LogWarning(
                "MCP 服务器 {Name} 未配置 AllowedTools 白名单且未显式允许全部，默认不暴露任何工具。" +
                "请在 MCP 配置中勾选需要的工具，或显式开启“允许全部”。",
                config.Name);

            foreach (var tool in availableTools)
            {
                _auditLogger.LogToolFiltered(config.Name, tool.Name, "白名单为空且未允许全部，默认不暴露");
            }
            return 0;
        }

        var loadedCount = 0;
        foreach (var tool in availableTools)
        {
            var toolName = tool.Name;
            if (!config.AllowAllTools &&
                !config.AllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            {
                _auditLogger.LogToolFiltered(config.Name, toolName, "不在允许列表中");
                continue;
            }

            _auditLogger.LogToolLoaded(config.Name, toolName, config.Category);
            destination.Add(tool);
            loadedCount++;
        }

        return loadedCount;
    }

    private static IClientTransport CreateStdioTransport(MCPServerConfig config)
    {
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = config.Name,
            Command = config.Command,
            Arguments = ParseStdioArguments(config.Arguments),
            EnvironmentVariables = config.EnvironmentVariables
        });
    }

    /// <summary>
    /// 解析 stdio 启动参数：支持双引号包裹含空格的参数（如 Windows 下 "C:\Program Files\..." 路径），
    /// 避免简单按空格切分导致带空格路径被拆散。
    /// </summary>
    private static string[] ParseStdioArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return [];

        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return [.. parts];
    }

    private static IClientTransport CreateSseTransport(MCPServerConfig config)
    {
        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = config.Name,
            TransportMode = HttpTransportMode.AutoDetect,
            Endpoint = new Uri(config.Command)
        });
    }

    private static IClientTransport CreateStreamableHttpTransport(MCPServerConfig config)
    {
        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = config.Name,
            TransportMode = HttpTransportMode.StreamableHttp,
            Endpoint = new Uri(config.Command)
        });
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record McpRuntime(
        string ServerName,
        string Fingerprint,
        IMcpClientSession Session,
        IReadOnlyList<AITool> Tools);
}

internal interface IMcpClientSessionFactory
{
    Task<IMcpClientSession> CreateAsync(
        MCPServerConfig config,
        CancellationToken cancellationToken);
}

internal interface IMcpClientSession : IAsyncDisposable
{
    Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken);
}

internal sealed class McpClientSessionFactory : IMcpClientSessionFactory
{
    public async Task<IMcpClientSession> CreateAsync(
        MCPServerConfig config,
        CancellationToken cancellationToken)
    {
        var transport = McpService.CreateClientTransport(config);
        var options = new McpClientOptions
        {
            ClientInfo = new() { Name = config.Name, Version = "1.0.0" }
        };
        var client = await McpClient.CreateAsync(
                transport,
                options,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new McpClientSession(client);
    }
}

internal sealed class McpClientSession(McpClient client) : IMcpClientSession
{
    public async Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return tools.Cast<AITool>().ToList();
    }

    public ValueTask DisposeAsync()
    {
        return client.DisposeAsync();
    }
}
