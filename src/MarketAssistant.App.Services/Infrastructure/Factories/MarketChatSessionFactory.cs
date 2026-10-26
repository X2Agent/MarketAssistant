using MarketAssistant.Agents;
using MarketAssistant.Agents.ContextProviders;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Services;
using MarketAssistant.Services.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// MarketChatSession 工厂接口
/// </summary>
public interface IMarketChatSessionFactory
{
    /// <summary>
    /// 创建新的 MarketChatSession 实例（每个聊天侧边栏对应一个独立会话）
    /// </summary>
    MarketChatSession Create(string? initialStockCode = null);
}

/// <summary>
/// MarketChatSession 工厂实现，从 DI 容器解析所有依赖并组装会话
/// </summary>
public class MarketChatSessionFactory(
    IChatClientFactory chatClientFactory,
    ILoggerFactory loggerFactory,
    McpToolContextProvider mcpToolProvider,
    GroundingSearchTools searchTools,
    MemoryManagementTools memoryTools,
    SessionSearchTools sessionSearchTools,
    KnowledgeGraphTools knowledgeGraphTools,
    TokenTrackingMiddleware tokenTracking,
    ConversationCompactionProviderFactory compactionProviderFactory,
    LayeredMemoryContextProvider layeredMemoryProvider,
    ChatSessionPersistenceService sessionPersistence,
    MemoryExtractionService memoryExtraction,
    AgentSkillsProvider? skillsProvider = null) : IMarketChatSessionFactory
{
    private readonly IChatClientFactory _chatClientFactory = chatClientFactory;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly McpToolContextProvider _mcpToolProvider = mcpToolProvider;
    private readonly GroundingSearchTools _searchTools = searchTools;
    private readonly MemoryManagementTools _memoryTools = memoryTools;
    private readonly SessionSearchTools _sessionSearchTools = sessionSearchTools;
    private readonly KnowledgeGraphTools _knowledgeGraphTools = knowledgeGraphTools;
    private readonly AgentSkillsProvider? _skillsProvider = skillsProvider;
    private readonly TokenTrackingMiddleware _tokenTracking = tokenTracking;
    private readonly ConversationCompactionProviderFactory _compactionProviderFactory = compactionProviderFactory;
    private readonly LayeredMemoryContextProvider _layeredMemoryProvider = layeredMemoryProvider;
    private readonly ChatSessionPersistenceService _sessionPersistence = sessionPersistence;
    private readonly MemoryExtractionService _memoryExtraction = memoryExtraction;

    public MarketChatSession Create(string? initialStockCode = null)
    {
        var runtime = _chatClientFactory.CreateRuntime();
        var logger = _loggerFactory.CreateLogger<MarketChatSession>();
        var compactionProvider = _compactionProviderFactory.Create(runtime.Client);

        return new MarketChatSession(
            runtime.Client,
            logger,
            mcpToolProvider: _mcpToolProvider,
            searchTools: _searchTools,
            memoryTools: _memoryTools,
            sessionSearchTools: _sessionSearchTools,
            knowledgeGraphTools: _knowledgeGraphTools,
            skillsProvider: _skillsProvider,
            tokenTracking: _tokenTracking,
            compactionProvider: compactionProvider,
            layeredMemoryProvider: _layeredMemoryProvider,
            sessionPersistence: _sessionPersistence,
            memoryExtraction: _memoryExtraction,
            initialStockCode: initialStockCode,
            providerId: runtime.ProviderId,
            modelId: runtime.ModelId,
            endpoint: runtime.Endpoint,
            runtimeConfigurationFingerprint: runtime.ConfigurationFingerprint);
    }
}
