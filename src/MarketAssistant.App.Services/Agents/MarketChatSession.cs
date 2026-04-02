using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Services.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace MarketAssistant.Agents;

/// <summary>
/// 市场对话会话管理器
/// 基于分析上下文的对话助手：分析结果由 MarketAnalysis Workflow 产出并注入为上下文，
/// ChatSession 负责基于上下文回答追问，可通过搜索工具补充最新信息。
/// MAF ChatClientAgent 通过 Function Calling 自动处理工具调用循环。
/// </summary>
public class MarketChatSession : IDisposable
{
    private readonly ChatClientAgent _agent;
    private readonly ILogger<MarketChatSession> _logger;
    private readonly McpService _mcpService;
    private readonly GroundingSearchTools? _searchTools;
    private readonly ConversationCompressor _compressor;
    private AgentSession? _currentSession;
    private readonly List<AITool> _allTools = [];
    private readonly List<ChatMessage> _conversationHistory = [];

    private string _currentStockCode = string.Empty;
    private string _analysisContext = string.Empty;
    private CancellationTokenSource? _currentCancellationTokenSource;
    private bool _disposed;
    private bool _toolsInitialized;
    private readonly SemaphoreSlim _toolsInitLock = new(1, 1);

    /// <summary>
    /// 当前会话估算的 Token 数
    /// </summary>
    public int EstimatedTokenCount => TokenEstimator.EstimateTotalTokens(_conversationHistory);

    public MarketChatSession(
        IChatClient chatClient,
        ILogger<MarketChatSession> logger,
        McpService mcpService,
        GroundingSearchTools? searchTools = null,
        AgentSkillsProvider? skillsProvider = null,
        string? initialStockCode = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mcpService = mcpService ?? throw new ArgumentNullException(nameof(mcpService));
        _searchTools = searchTools;
        _currentStockCode = initialStockCode ?? string.Empty;
        _compressor = new ConversationCompressor(chatClient, logger);

        _agent = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "MarketAnalyst",
                ChatOptions = new ChatOptions
                {
                    Instructions = BuildAgentInstructions(),
                    Temperature = 0.7f
                },
                AIContextProviders = skillsProvider != null ? [skillsProvider] : null
            });

        _logger.LogInformation("MarketChatSession 初始化完成（工具待异步加载）");
    }

    #region 工具初始化

    private async Task EnsureToolsInitializedAsync()
    {
        if (_toolsInitialized) return;

        await _toolsInitLock.WaitAsync();
        try
        {
            if (_toolsInitialized) return;

            if (_searchTools != null)
            {
                _allTools.AddRange(_searchTools.GetFunctions().Select(f => (AITool)f));
                _logger.LogInformation("加载搜索工具，数量: {Count}", _allTools.Count);
            }

            try
            {
                var enabledConfigs = _mcpService.GetEnabledConfigs();
                var mcpTools = await _mcpService.GetAIToolsAsync(enabledConfigs);
                _allTools.AddRange(mcpTools);
                _logger.LogInformation("加载 MCP 工具，数量: {Count}", mcpTools.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化 MCP 工具失败");
            }

            _toolsInitialized = true;
            _logger.LogInformation("工具初始化完成，总数: {Count}", _allTools.Count);
        }
        finally
        {
            _toolsInitLock.Release();
        }
    }

    #endregion

    #region 公共属性

    /// <summary>
    /// 获取对话历史（自维护的消息镜像）
    /// </summary>
    public Task<IReadOnlyList<ChatMessage>> GetConversationHistoryAsync()
    {
        return Task.FromResult<IReadOnlyList<ChatMessage>>(_conversationHistory.AsReadOnly());
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
        _currentSession = null;
        _conversationHistory.Clear();

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
    public async IAsyncEnumerable<StreamingChatUpdate> SendMessageStreamAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始流式处理用户消息: {Message}", userMessage);

        await EnsureToolsInitializedAsync();

        if (_compressor.NeedsCompression(_conversationHistory))
        {
            _logger.LogInformation("会话 Token 超过阈值，触发自动压缩");
            var compressed = await _compressor.CompressAsync(
                _conversationHistory, _analysisContext, cancellationToken);
            _conversationHistory.Clear();
            _conversationHistory.AddRange(compressed);
            _currentSession = null;
        }

        _currentSession ??= await _agent.CreateSessionAsync(cancellationToken: cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentCancellationTokenSource = cts;

        _conversationHistory.Add(new ChatMessage(ChatRole.User, userMessage));

        var completeResponse = new StringBuilder();

        var runOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools = _allTools,
                Instructions = BuildAgentInstructions()
            }
        };

        var streamingUpdates = _agent.RunStreamingAsync(
            message: userMessage,
            session: _currentSession,
            options: runOptions,
            cancellationToken: cts.Token);

        await foreach (var update in streamingUpdates.ConfigureAwait(false))
        {
            var content = update.Text ?? string.Empty;
            if (!string.IsNullOrEmpty(content))
            {
                completeResponse.Append(content);
            }
            yield return new StreamingChatUpdate { Content = content };
        }

        _conversationHistory.Add(new ChatMessage(ChatRole.Assistant, completeResponse.ToString()));

        _logger.LogInformation("流式 AI 回复完成，长度: {Length}", completeResponse.Length);
        _currentCancellationTokenSource = null;
    }

    public void ClearHistory()
    {
        _currentSession = null;
        _conversationHistory.Clear();
        _analysisContext = string.Empty;
        _logger.LogInformation("清除聊天历史（重置 Session 和上下文）");
    }

    public void SetCurrentStock(string stockCode)
    {
        _currentStockCode = stockCode;
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
            </instructions>

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

        return sb.ToString();
    }

    #endregion

    #region IDisposable

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _currentCancellationTokenSource?.Cancel();
                _currentCancellationTokenSource?.Dispose();
                _toolsInitLock.Dispose();
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

/// <summary>
/// 流式聊天更新
/// </summary>
public class StreamingChatUpdate
{
    public string Content { get; set; } = string.Empty;
}
