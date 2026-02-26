using MarketAssistant.Services.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace MarketAssistant.Agents;

/// <summary>
/// 市场对话会话管理器（MAF 优化版本）
/// 使用 Microsoft Agent Framework 的 ChatClientAgent 实现自动工具调用
/// </summary>
public class MarketChatSession : IDisposable
{
    #region 私有字段

    private readonly ChatClientAgent _agent;
    private readonly ILogger<MarketChatSession> _logger;
    private readonly McpService _mcpService;
    private AgentSession? _currentSession;
    private readonly List<AITool> _mcpTools = new();

    private string _currentStockCode = string.Empty;
    private CancellationTokenSource? _currentCancellationTokenSource;
    private bool _disposed;
    private bool _toolsInitialized;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建市场对话会话（MAF 优化版本）
    /// </summary>
    public MarketChatSession(
        IChatClient chatClient,
        ILogger<MarketChatSession> logger,
        McpService mcpService,
        string? initialStockCode = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mcpService = mcpService ?? throw new ArgumentNullException(nameof(mcpService));
        _currentStockCode = initialStockCode ?? string.Empty;

        // 创建 ChatClientAgent（延迟工具初始化）
        _agent = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "MarketAnalyst",
                ChatOptions = new ChatOptions
                {
                    Instructions = BuildAgentInstructions(),
                    Temperature = 0.7f
                    // Tools 将在异步初始化后设置
                }
            });

        _logger.LogInformation("MarketChatSession 初始化完成（工具待异步加载）");
    }

    #endregion

    #region 工具初始化

    /// <summary>
    /// 异步初始化 MCP 工具
    /// </summary>
    private async Task EnsureToolsInitializedAsync()
    {
        if (_toolsInitialized)
        {
            return;
        }

        try
        {
            var enabledConfigs = McpService.GetEnabledConfigs();
            var mcpToolsEnumerable = await _mcpService.GetAIToolsAsync(enabledConfigs);
            _mcpTools.AddRange(mcpToolsEnumerable);

            _toolsInitialized = true;
            _logger.LogInformation("成功加载 MCP 工具，数量: {Count}", _mcpTools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 MCP 工具失败");
            _toolsInitialized = true; // 标记为已尝试，避免重复失败
        }
    }

    #endregion

    #region 公共属性

    /// <summary>
    /// 获取对话历史
    /// </summary>
    public Task<IReadOnlyList<ChatMessage>> GetConversationHistoryAsync()
    {
        // 历史记录由框架内部的 InMemoryChatHistoryProvider 管理，通过 session 保存
        return Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());
    }

    /// <summary>
    /// 当前股票代码
    /// </summary>
    public string CurrentStockCode => _currentStockCode;

    /// <summary>
    /// 是否正在处理请求
    /// </summary>
    public bool IsProcessing => _currentCancellationTokenSource != null &&
                                !_currentCancellationTokenSource.Token.IsCancellationRequested;

    #endregion

    #region 公共方法

    /// <summary>
    /// 发送消息并获取流式回复（MAF 自动处理工具调用）
    /// </summary>
    public async IAsyncEnumerable<StreamingChatUpdate> SendMessageStreamAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始流式处理用户消息: {Message}", userMessage);

        // 确保工具已初始化
        await EnsureToolsInitializedAsync();

        // 确保 Session 已创建
        _currentSession ??= await _agent.CreateSessionAsync(cancellationToken: cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentCancellationTokenSource = cts;

        var completeResponse = new StringBuilder();

        var runOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools = _mcpTools,
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

        _logger.LogInformation("流式 AI 回复完成，长度: {Length}", completeResponse.Length);
        _currentCancellationTokenSource = null;
    }

    /// <summary>
    /// 清除聊天历史
    /// </summary>
    public void ClearHistory()
    {
        // 置空 session，下次发送消息时会重新创建
        _currentSession = null;
        _logger.LogInformation("清除聊天历史（重置 Session）");
    }

    /// <summary>
    /// 更新当前股票代码
    /// </summary>
    public void SetCurrentStock(string stockCode)
    {
        _currentStockCode = stockCode;
        _logger.LogInformation("设置当前股票: {StockCode}", stockCode);
    }

    /// <summary>
    /// 停止当前正在进行的请求
    /// </summary>
    public void StopCurrentRequest()
    {
        _currentCancellationTokenSource?.Cancel();
        _logger.LogInformation("停止当前请求");
    }

    #endregion

    #region 提示词构建

    private string BuildAgentInstructions()
    {
        var stockInfo = _currentStockCode.Length > 0 ? _currentStockCode : "未指定";

        return @$"<role>
你是专业的金融市场分析师，擅长股票、期货、加密货币等资产的多维度分析。
当前关注标的：{stockInfo}
</role>

    <react>
    你必须使用 ReAct 模式完成任务，循环格式如下：
    Thought: [思考，必须包含：已有信息、缺失信息、工具选择理由、参数准备、逻辑连贯性]
    Action: [调用工具及参数]
    Observation: [工具返回结果]

    当信息充足时结束循环并输出：
    Final Answer: [完整、最终的答案，不允许以问句结尾]
    </react>

<capabilities>
你可以调用工具完成分析任务：
- 基础数据：价格、K线、市值、成交量
- 财务数据：财报、盈利、现金流、资产负债
- 技术指标：MA、MACD、RSI、BOLL
- 新闻情绪：实时新闻、社交媒体、市场热度
- 筛选排名：资金流向、涨跌幅、异动监控
- Web 搜索：市场资讯、行业研究、政策动向
</capabilities>

<response_format>
【核心观点】简洁结论（20字以内）
【数据支撑】关键数值与指标（含数据来源）
【技术分析】图表形态、指标背离、支撑压力位
【风险提示】潜在下行风险与不确定性
</response_format>

<quality_standards>
1. **数据引用**：必须注明时间与来源
2. **数值精确**：价格保留2位小数，百分比保留1位小数
3. **多维验证**：技术面 + 基本面 + 情绪面交叉分析
4. **历史对比**：同比/环比/与历史高点低点对比
</quality_standards>

<禁止表达>
不要说""好的""、""当然""、""我来帮你""等客套话
不要以问句结尾（如""需要我进一步分析吗？""）
不要说""以上是我的分析""、""希望对你有帮助""
直接给出分析结论，避免冗余铺垫
</禁止表达>";
    }

    #endregion

    #region IDisposable 实现

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _currentCancellationTokenSource?.Cancel();
                _currentCancellationTokenSource?.Dispose();
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
