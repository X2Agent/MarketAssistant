using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Mcp;

/// <summary>
/// MCP 工具上下文提供者，实现 MAF AIContextProvider 模式。
/// 在每次 Agent 调用前自动将 MCP 服务器工具注入到 AIContext.Tools，
/// 无需调用方手动管理工具加载和传递。
/// 支持缓存、按需刷新和连接状态监控。
/// </summary>
public sealed class McpToolContextProvider : AIContextProvider
{
    private readonly McpService _mcpService;
    private readonly ILogger<McpToolContextProvider> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private List<AITool>? _cachedTools;
    private bool _invalidated;

    public McpToolContextProvider(
        McpService mcpService,
        ILogger<McpToolContextProvider> logger)
    {
        _mcpService = mcpService ?? throw new ArgumentNullException(nameof(mcpService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 已加载的 MCP 工具数量
    /// </summary>
    public int LoadedToolCount => _cachedTools?.Count ?? 0;

    /// <summary>
    /// 标记工具缓存失效，下次 Agent 调用时重新加载。
    /// 配置变更（新增/删除/修改 MCP 服务器）后应调用此方法。
    /// </summary>
    public void Invalidate()
    {
        _invalidated = true;
        _logger.LogInformation("MCP 工具缓存已标记失效，将在下次调用时刷新");
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var tools = await GetOrLoadToolsAsync(cancellationToken);

        return new AIContext
        {
            Tools = tools
        };
    }

    /// <summary>
    /// 获取或加载 MCP 工具（线程安全，支持失效刷新）
    /// </summary>
    private async Task<List<AITool>> GetOrLoadToolsAsync(CancellationToken cancellationToken)
    {
        if (_cachedTools != null && !_invalidated)
            return _cachedTools;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // double-check
            if (_cachedTools != null && !_invalidated)
                return _cachedTools;

            if (_invalidated)
            {
                // 释放旧连接后重新加载
                await _mcpService.ResetConnectionsAsync();
                _invalidated = false;
            }

            var enabledConfigs = _mcpService.GetEnabledConfigs();
            if (enabledConfigs.Count == 0)
            {
                _cachedTools = [];
                return _cachedTools;
            }

            _cachedTools = await _mcpService.GetAIToolsAsync(enabledConfigs);
            _logger.LogInformation("McpToolContextProvider 加载了 {Count} 个 MCP 工具", _cachedTools.Count);

            return _cachedTools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "McpToolContextProvider 加载 MCP 工具失败");
            _cachedTools ??= [];
            return _cachedTools;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
