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
public class MarketChatSessionFactory : IMarketChatSessionFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly McpToolContextProvider _mcpToolProvider;
    private readonly GroundingSearchTools _searchTools;
    private readonly MemoryManagementTools _memoryTools;
    private readonly SessionSearchTools _sessionSearchTools;
    private readonly KnowledgeGraphTools _knowledgeGraphTools;
    private readonly AgentSkillsProvider? _skillsProvider;
    private readonly TokenTrackingMiddleware _tokenTracking;
    private readonly ConversationCompressionMiddleware _compressionMiddleware;
    private readonly LayeredMemoryContextProvider _layeredMemoryProvider;
    private readonly RagContextProvider _ragProvider;
    private readonly ChatSessionPersistenceService _sessionPersistence;
    private readonly MemoryExtractionService _memoryExtraction;

    public MarketChatSessionFactory(
        IChatClientFactory chatClientFactory,
        ILoggerFactory loggerFactory,
        McpToolContextProvider mcpToolProvider,
        GroundingSearchTools searchTools,
        MemoryManagementTools memoryTools,
        SessionSearchTools sessionSearchTools,
        KnowledgeGraphTools knowledgeGraphTools,
        TokenTrackingMiddleware tokenTracking,
        ConversationCompressionMiddleware compressionMiddleware,
        LayeredMemoryContextProvider layeredMemoryProvider,
        RagContextProvider ragProvider,
        ChatSessionPersistenceService sessionPersistence,
        MemoryExtractionService memoryExtraction,
        AgentSkillsProvider? skillsProvider = null)
    {
        _chatClientFactory = chatClientFactory;
        _loggerFactory = loggerFactory;
        _mcpToolProvider = mcpToolProvider;
        _searchTools = searchTools;
        _memoryTools = memoryTools;
        _sessionSearchTools = sessionSearchTools;
        _knowledgeGraphTools = knowledgeGraphTools;
        _skillsProvider = skillsProvider;
        _tokenTracking = tokenTracking;
        _compressionMiddleware = compressionMiddleware;
        _layeredMemoryProvider = layeredMemoryProvider;
        _ragProvider = ragProvider;
        _sessionPersistence = sessionPersistence;
        _memoryExtraction = memoryExtraction;
    }

    public MarketChatSession Create(string? initialStockCode = null)
    {
        var chatClient = _chatClientFactory.CreateClient();
        var logger = _loggerFactory.CreateLogger<MarketChatSession>();

        return new MarketChatSession(
            chatClient,
            logger,
            mcpToolProvider: _mcpToolProvider,
            searchTools: _searchTools,
            memoryTools: _memoryTools,
            sessionSearchTools: _sessionSearchTools,
            knowledgeGraphTools: _knowledgeGraphTools,
            skillsProvider: _skillsProvider,
            tokenTracking: _tokenTracking,
            compressionMiddleware: _compressionMiddleware,
            layeredMemoryProvider: _layeredMemoryProvider,
            ragProvider: _ragProvider,
            sessionPersistence: _sessionPersistence,
            memoryExtraction: _memoryExtraction,
            initialStockCode: initialStockCode);
    }
}
