using MarketAssistant.Agents.ContextProviders;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.Services.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace MarketAssistant.Agents;

/// <summary>
/// 市场对话会话管理器
/// 基于分析上下文的对话助手：分析结果由 MarketAnalysis Workflow 产出并注入为上下文，
/// ChatSession 负责基于上下文回答追问，可通过搜索工具补充最新信息。
/// MAF ChatClientAgent 通过 Function Calling 自动处理工具调用循环。
/// 通过 MAF Middleware 实现 Token 追踪与自动会话压缩。
/// </summary>
public class MarketChatSession : IDisposable
{
    private const int SessionSchemaVersion = 1;
    private static readonly JsonSerializerOptions SessionSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AIAgent _agent;
    private readonly ILogger<MarketChatSession> _logger;
    private readonly GroundingSearchTools? _searchTools;
    private readonly MemoryManagementTools? _memoryTools;
    private readonly SessionSearchTools? _sessionSearchTools;
    private readonly KnowledgeGraphTools? _knowledgeGraphTools;
    private readonly ChatSessionPersistenceService? _sessionPersistence;
    private readonly MemoryExtractionService? _memoryExtraction;
    private readonly string _providerId;
    private readonly string _modelId;
    private readonly string _endpoint;
    private readonly string _runtimeConfigurationFingerprint;
    private int _turnsSinceLastExtraction;
    private AgentSession? _currentSession;
    private readonly List<AITool> _searchToolCache = [];
    private readonly List<ChatMessage> _conversationHistory = [];
    private readonly object _conversationLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private string _sessionId = Guid.NewGuid().ToString("N");
    private string _currentStockCode = string.Empty;
    private string _analysisContext = string.Empty;
    private string? _cachedInstructions;
    private CancellationTokenSource? _currentCancellationTokenSource;
    private bool _disposed;
    private bool _searchToolsInitialized;
    private bool _restoreHistoryOnNextRun;

    /// <summary>
    /// 当前会话 ID（用于持久化标识）
    /// </summary>
    public string SessionId => _sessionId;

    /// <summary>
    /// 当前会话估算的 Token 数（优先从 Session StateBag 中间件数据读取，回退到本地估算）
    /// </summary>
    public long EstimatedTokenCount
    {
        get
        {
            var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(_currentSession);
            if (input + output > 0)
                return input + output;

            lock (_conversationLock)
            {
                return TokenEstimator.EstimateTotalTokens(_conversationHistory);
            }
        }
    }

    public MarketChatSession(
        IChatClient chatClient,
        ILogger<MarketChatSession> logger,
        McpToolContextProvider? mcpToolProvider = null,
        GroundingSearchTools? searchTools = null,
        MemoryManagementTools? memoryTools = null,
        SessionSearchTools? sessionSearchTools = null,
        KnowledgeGraphTools? knowledgeGraphTools = null,
        AgentSkillsProvider? skillsProvider = null,
        TokenTrackingMiddleware? tokenTracking = null,
        AIContextProvider? compactionProvider = null,
        LayeredMemoryContextProvider? layeredMemoryProvider = null,
        ChatSessionPersistenceService? sessionPersistence = null,
        MemoryExtractionService? memoryExtraction = null,
        string? initialStockCode = null,
        string? providerId = null,
        string? modelId = null,
        string? endpoint = null,
        string? runtimeConfigurationFingerprint = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _searchTools = searchTools;
        _memoryTools = memoryTools;
        _sessionSearchTools = sessionSearchTools;
        _knowledgeGraphTools = knowledgeGraphTools;
        _sessionPersistence = sessionPersistence;
        _memoryExtraction = memoryExtraction;
        _currentStockCode = initialStockCode ?? string.Empty;
        _providerId = providerId ?? string.Empty;
        _modelId = modelId ?? string.Empty;
        _endpoint = endpoint ?? string.Empty;
        _runtimeConfigurationFingerprint = runtimeConfigurationFingerprint ?? string.Empty;

        // 收集所有 AIContextProvider：Skills + MCP 工具 + LayeredMemory (优先) / Memory + RAG
        var contextProviders = new List<AIContextProvider>();
        if (skillsProvider != null) contextProviders.Add(skillsProvider);
        if (mcpToolProvider != null) contextProviders.Add(mcpToolProvider);
        if (layeredMemoryProvider != null)
            contextProviders.Add(layeredMemoryProvider);
        if (compactionProvider != null)
            contextProviders.Add(compactionProvider);

        var baseAgent = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "MarketAnalyst",
                ChatOptions = new ChatOptions
                {
                    Instructions = BuildAgentInstructions(),
                    Temperature = 0.7f
                },
                AIContextProviders = contextProviders.Count > 0 ? [.. contextProviders] : null
            });

        // Compaction 作为会话级 AIContextProvider 参与 Session 序列化；
        // Token Tracking 仍作为无状态 Agent Middleware 附加。
        _agent = BuildAgentWithMiddleware(baseAgent, tokenTracking);

        _logger.LogInformation("MarketChatSession 初始化完成（工具待异步加载，已附加中间件）");
    }

    /// <summary>
    /// 使用 MAF AsBuilder 模式为 Agent 附加 Token 追踪中间件。
    /// </summary>
    private static AIAgent BuildAgentWithMiddleware(
        AIAgent baseAgent,
        TokenTrackingMiddleware? tokenTracking)
    {
        if (tokenTracking is null)
            return baseAgent;

        return baseAgent
            .AsBuilder()
            .Use(
                runFunc: tokenTracking.InvokeAsync,
                runStreamingFunc: tokenTracking.InvokeStreamingAsync)
            .Build();
    }

    #region 工具初始化

    private void EnsureSearchToolsInitialized()
    {
        if (_searchToolsInitialized) return;

        if (_searchTools != null)
            _searchToolCache.AddRange(_searchTools.GetFunctions().Select(f => (AITool)f));

        if (_memoryTools != null)
            _searchToolCache.AddRange(_memoryTools.GetFunctions().Select(f => (AITool)f));

        if (_sessionSearchTools != null)
            _searchToolCache.AddRange(_sessionSearchTools.GetFunctions().Select(f => (AITool)f));

        if (_knowledgeGraphTools != null)
            _searchToolCache.AddRange(_knowledgeGraphTools.GetFunctions().Select(f => (AITool)f));

        _logger.LogInformation("加载工具完成，数量: {Count}", _searchToolCache.Count);

        // MCP 工具通过 McpToolContextProvider（AIContextProvider）自动注入，无需手动加载
        _searchToolsInitialized = true;
    }

    #endregion

    #region 公共属性

    /// <summary>
    /// 获取对话历史（自维护的消息镜像）
    /// </summary>
    public Task<IReadOnlyList<ChatMessage>> GetConversationHistoryAsync()
    {
        lock (_conversationLock)
        {
            return Task.FromResult<IReadOnlyList<ChatMessage>>(_conversationHistory.AsReadOnly());
        }
    }

    public string CurrentStockCode => _currentStockCode;

    public bool IsProcessing => _currentCancellationTokenSource != null &&
                                !_currentCancellationTokenSource.Token.IsCancellationRequested;

    #endregion

    #region 分析上下文注入

    /// <summary>
    /// 注入分析上下文，将 Workflow 阶段的分析结果作为对话背景。
    /// 调用后重置会话，后续对话将基于此上下文进行。
    /// </summary>
    public void InjectAnalysisContext(string stockCode, IEnumerable<ChatMessage> analysisMessages)
    {
        _currentStockCode = stockCode;
        _analysisContext = BuildAnalysisSummary(analysisMessages);
        _cachedInstructions = null;
        _currentSession = null;
        _restoreHistoryOnNextRun = false;
        lock (_conversationLock)
        {
            _conversationHistory.Clear();
        }

        _logger.LogInformation(
            "注入分析上下文，标的: {StockCode}，摘要长度: {Length}",
            stockCode, _analysisContext.Length);
    }

    /// <summary>
    /// 将多位分析师的分析结果提炼为结构化摘要
    /// </summary>
    private static string BuildAnalysisSummary(IEnumerable<ChatMessage> analysisMessages)
    {
        var sb = new StringBuilder();
        int index = 0;

        foreach (var message in analysisMessages)
        {
            var text = message.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            index++;
            var author = message.AuthorName ?? $"分析师{index}";
            sb.AppendLine($"### {author}");
            sb.AppendLine(text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion

    #region 对话方法

    /// <summary>
    /// 发送消息并获取流式回复（MAF 通过 Function Calling 自动处理工具调用）
    /// </summary>
    public async IAsyncEnumerable<string> SendMessageStreamAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        if (!await _sendLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("当前会话正在处理另一条消息，请等待完成或先停止当前请求");

        var completeResponse = new StringBuilder();
        var userMessageAdded = false;
        var completed = false;
        CancellationTokenSource? cts = null;
        using var activity = MarketAssistantDiagnostics.StartActivity("market_chat.agent.run");
        activity?.SetTag("gen_ai.provider.name", _providerId);
        activity?.SetTag("gen_ai.request.model", _modelId);
        activity?.SetTag("marketassistant.session.id", _sessionId);
        activity?.SetTag("marketassistant.asset.symbol", _currentStockCode);

        try
        {
            EnsureSearchToolsInitialized();

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentCancellationTokenSource = cts;
            _currentSession ??= await _agent.CreateSessionAsync(cancellationToken: cts.Token);

            var currentUserMessage = new ChatMessage(ChatRole.User, userMessage);
            IReadOnlyList<ChatMessage> runMessages;
            lock (_conversationLock)
            {
                runMessages = _restoreHistoryOnNextRun
                    ? [.. _conversationHistory, currentUserMessage]
                    : [currentUserMessage];
                _conversationHistory.Add(currentUserMessage);
                userMessageAdded = true;
            }
            _restoreHistoryOnNextRun = false;

            var runOptions = new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions
                {
                    Tools = _searchToolCache.Count > 0 ? _searchToolCache : null,
                    Instructions = BuildAgentInstructions()
                }
            };

            var streamingUpdates = _agent.RunStreamingAsync(
                messages: runMessages,
                session: _currentSession,
                options: runOptions,
                cancellationToken: cts.Token);

            await foreach (var update in streamingUpdates.ConfigureAwait(false))
            {
                var content = update.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(content))
                    completeResponse.Append(content);

                yield return content;
            }
            completed = true;
        }
        finally
        {
            if (userMessageAdded)
            {
                lock (_conversationLock)
                {
                    var responseText = completeResponse.ToString();
                    _conversationHistory.Add(new ChatMessage(
                        ChatRole.Assistant,
                        completed
                            ? responseText
                            : responseText.Length > 0
                                ? responseText + "\n\n[回复被中断]"
                                : "[回复被中断]"));
                }
            }

            activity?.SetTag("gen_ai.response.output_length", completeResponse.Length);
            if (!completed)
            {
                // MAF 可能已将部分流式回复写入 Session。丢弃该 Session，下一轮从带中断标记的
                // UI 历史镜像单次回放，避免内部历史与用户可见历史分叉。
                _currentSession = null;
                _restoreHistoryOnNextRun = userMessageAdded;
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "interrupted");
            }
            else
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
            }

            if (ReferenceEquals(_currentCancellationTokenSource, cts))
                _currentCancellationTokenSource = null;

            cts?.Dispose();
            _sendLock.Release();
        }

        _logger.LogInformation("流式 AI 回复完成，长度: {Length}", completeResponse.Length);
        await AutoSaveSessionAsync(CancellationToken.None);
    }

    /// <summary>
    /// 从持久化存储恢复会话
    /// </summary>
    public async Task<bool> RestoreSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessionPersistence is null) return false;

        var snapshot = await _sessionPersistence.LoadSessionAsync(sessionId, cancellationToken);
        if (snapshot is null) return false;

        _sessionId = snapshot.Id;
        _currentStockCode = snapshot.StockCode;
        _analysisContext = snapshot.AnalysisContext ?? string.Empty;
        _cachedInstructions = null;
        int messageCount;
        lock (_conversationLock)
        {
            _conversationHistory.Clear();
            foreach (var dto in snapshot.Messages)
            {
                _conversationHistory.Add(new ChatMessage(new ChatRole(dto.Role), dto.Content)
                {
                    AuthorName = dto.AuthorName
                });
            }
            messageCount = _conversationHistory.Count;
        }

        _currentSession = null;
        _restoreHistoryOnNextRun = messageCount > 0;
        if (CanRestoreAgentSession(snapshot))
        {
            try
            {
                _currentSession = await _agent.DeserializeSessionAsync(
                    snapshot.AgentSessionState!.Value,
                    SessionSerializerOptions,
                    cancellationToken);
                _restoreHistoryOnNextRun = false;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                _logger.LogWarning(
                    ex,
                    "恢复 MAF Session 失败，将在下一轮回放 UI 历史。SessionId: {SessionId}",
                    sessionId);
            }
        }

        _logger.LogInformation(
            "恢复会话 {SessionId}，消息数: {Count}，MAF Session: {SessionRestored}",
            sessionId,
            messageCount,
            _currentSession is not null);
        return true;
    }

    private async Task AutoSaveSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionPersistence is null) return;

        try
        {
            List<ChatMessage> historyCopy;
            lock (_conversationLock)
            {
                historyCopy = _conversationHistory.ToList();
            }

            JsonElement? agentSessionState = null;
            if (_currentSession is not null)
            {
                agentSessionState = await _agent.SerializeSessionAsync(
                    _currentSession,
                    SessionSerializerOptions,
                    cancellationToken);
            }

            var snapshot = new ChatSessionSnapshot
            {
                Id = _sessionId,
                StockCode = _currentStockCode,
                Title = BuildSessionTitle(historyCopy),
                AnalysisContext = _analysisContext,
                Messages = historyCopy.Select(m => new ChatMessageDto
                {
                    Role = m.Role.Value,
                    Content = m.Text ?? string.Empty,
                    AuthorName = m.AuthorName
                }).ToList(),
                AgentSessionState = agentSessionState,
                SessionSchemaVersion = SessionSchemaVersion,
                ProviderId = _providerId,
                ModelId = _modelId,
                Endpoint = _endpoint,
                RuntimeConfigurationFingerprint = _runtimeConfigurationFingerprint
            };
            await _sessionPersistence.SaveSessionAsync(snapshot, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动保存会话失败");
        }

        // 每 N 轮对话触发一次自动记忆提取（后台执行，不阻塞用户）
        _turnsSinceLastExtraction++;
        if (_memoryExtraction != null &&
            _turnsSinceLastExtraction >= _memoryExtraction.ExtractionInterval)
        {
            _turnsSinceLastExtraction = 0;
            List<ChatMessage> extractionSnapshot;
            lock (_conversationLock)
            {
                extractionSnapshot = _conversationHistory.ToList();
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await _memoryExtraction.ExtractAndSaveAsync(extractionSnapshot, ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "后台记忆提取失败");
                }
            }, CancellationToken.None);
        }
    }

    private bool CanRestoreAgentSession(ChatSessionSnapshot snapshot)
    {
        return snapshot.AgentSessionState is not null &&
               snapshot.SessionSchemaVersion == SessionSchemaVersion &&
               !string.IsNullOrEmpty(_runtimeConfigurationFingerprint) &&
               string.Equals(
                   snapshot.RuntimeConfigurationFingerprint,
                   _runtimeConfigurationFingerprint,
                   StringComparison.Ordinal);
    }

    private string BuildSessionTitle(List<ChatMessage> history)
    {
        var firstUserMsg = history.FirstOrDefault(m => m.Role == ChatRole.User);
        var title = firstUserMsg?.Text ?? _currentStockCode;
        return title.Length > 50 ? title[..50] + "…" : title;
    }

    public void ClearHistory()
    {
        _currentSession = null;
        _restoreHistoryOnNextRun = false;
        lock (_conversationLock)
        {
            _conversationHistory.Clear();
        }
        _analysisContext = string.Empty;
        _logger.LogInformation("清除聊天历史（重置 Session 和上下文）");
    }

    public void SetCurrentStock(string stockCode)
    {
        _currentStockCode = stockCode;
        _cachedInstructions = null;
        _logger.LogInformation("设置当前标的: {StockCode}", stockCode);
    }

    public void StopCurrentRequest()
    {
        _currentCancellationTokenSource?.Cancel();
        _logger.LogInformation("停止当前请求");
    }

    #endregion

    #region 提示词构建

    private string BuildAgentInstructions()
    {
        if (_cachedInstructions != null)
            return _cachedInstructions;

        var sb = new StringBuilder();

        sb.AppendLine("<role>");
        sb.AppendLine("你是专业的金融市场对话助手。");
        if (_currentStockCode.Length > 0)
            sb.AppendLine($"当前关注标的：{_currentStockCode}");
        sb.AppendLine("</role>");
        sb.AppendLine();

        if (_analysisContext.Length > 0)
        {
            sb.AppendLine("<analysis_context>");
            sb.AppendLine("以下是多位专业分析师对当前标的的深度分析报告，你的回答应优先基于这些分析结果：");
            sb.AppendLine();
            sb.AppendLine(_analysisContext);
            sb.AppendLine("</analysis_context>");
            sb.AppendLine();
        }

        sb.AppendLine("""
            <instructions>
            1. 优先基于分析上下文回答用户问题，引用分析结果时注明来源（如"技术分析显示..."、"基本面数据表明..."）
            2. 当需要最新信息（实时新闻、市场动态、政策变化）时，使用搜索工具补充
            3. 综合已有分析和搜索结果给出完整、有理有据的回答
            4. 如果分析上下文中没有相关信息且工具也无法获取，坦诚说明
            5. 当用户提到过去讨论过的内容时，使用 SearchPastSessionsAsync 搜索历史对话来回忆
            </instructions>

            <memory_protocol>
            你有一个持久化记忆系统，可以跨会话记住用户信息。请在以下情况主动保存记忆：
            - 用户明确表达投资偏好或风格时（category=preference，如"我偏好价值投资"、"我不做短线"）
            - 用户纠正你的分析或认知时（category=correction，如"不对，我更看重现金流"）
            - 得出重要分析结论时（category=conclusion，如某标的的关键发现）
            - 了解到用户身份信息时（category=profile，如职业、经验水平、资金规模）
            保存时 key 要简短唯一，value 要简洁信息密集。不要保存显而易见的信息或临时性内容。
            重要的记忆使用后不要反复保存，先查询确认是否已存在。
            对于特别重要的用户信息（如核心投资偏好、职业背景），保存后使用 SetMemoryPriorityAsync 将其设为高优先级(1)，
            高优先级记忆会始终加载到上下文中，确保每次对话都能参考。
            </memory_protocol>

            <knowledge_graph_protocol>
            你有一个知识图谱系统，用于记录实体之间的结构化关系。请在以下情况记录关系：
            - 用户表示关注或持有某标的时（如 用户 --[持有]--> 贵州茅台）
            - 完成标的分析后（如 用户 --[分析过]--> 比特币）
            - 发现重要的行业/事件关联时（如 降息 --[影响]--> 银行板块）
            - 用户提到不再持有某标的时，使用 InvalidateRelationAsync 标记过期
            查询用户关注的标的时使用 QueryEntityAsync，回顾投资历史使用 GetTimelineAsync。
            </knowledge_graph_protocol>

            <quality_standards>
            1. 数值精确：价格保留2位小数，百分比保留1位小数
            2. 注明数据来源和时间
            3. 区分事实与观点
            </quality_standards>

            <forbidden>
            不要说"好的"、"当然"、"我来帮你"等客套话
            不要以问句结尾（如"需要我进一步分析吗？"）
            不要说"以上是我的分析"、"希望对你有帮助"
            直接给出结论，避免冗余铺垫
            </forbidden>
            """);

        _cachedInstructions = sb.ToString();
        return _cachedInstructions;
    }

    #endregion

    #region IDisposable

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 正在发送时，CTS 和发送门闩由 SendMessageStreamAsync 的 finally 统一收尾，
                // Dispose 只负责发出取消信号，避免并发 Dispose/Release 竞态。
                _currentCancellationTokenSource?.Cancel();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

