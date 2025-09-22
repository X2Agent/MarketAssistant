using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace MarketAssistant.Agents;

/// <summary>
/// 市场对话代理，专门处理用户与AI的对话交互
/// </summary>
public class MarketChatAgent
{
    #region 常量定义

    /// <summary>
    /// 最大上下文消息数量
    /// </summary>
    private const int MaxContextMessages = 100;

    /// <summary>
    /// 压缩后保留的最小消息数量
    /// </summary>
    private const int MinMessagesAfterCompression = 10;

    #endregion

    #region 私有字段

    private readonly IChatCompletionService _chatCompletionService;
    private readonly MarketAnalysisAgent? _analysisAgent;
    private readonly ILogger<MarketChatAgent> _logger;
    private readonly Kernel _kernel;

    /// <summary>
    /// 对话历史记录
    /// </summary>
    private readonly ChatHistory _conversationHistory = new();

    /// <summary>
    /// 当前股票代码
    /// </summary>
    private string _currentStockCode = string.Empty;


    /// <summary>
    /// 取消令牌源
    /// </summary>
    private CancellationTokenSource? _currentCancellationTokenSource;

    #endregion

    #region 构造函数

    public MarketChatAgent(
        IChatCompletionService chatCompletionService,
        ILogger<MarketChatAgent> logger,
        Kernel kernel,
        MarketAnalysisAgent? analysisAgent = null)
    {
        _chatCompletionService = chatCompletionService;
        _analysisAgent = analysisAgent;
        _logger = logger;
        _kernel = kernel;

        // 初始化系统消息
        InitializeSystemContext();
    }

    #endregion

    #region 事件定义

    /// <summary>
    /// 流式响应事件
    /// </summary>
    public event EventHandler<StreamingResponseEventArgs>? StreamingResponse;

    /// <summary>
    /// 上下文压缩事件
    /// </summary>
    public event EventHandler<ContextCompressionEventArgs>? ContextCompressed;


    #endregion

    #region 公共属性

    /// <summary>
    /// 获取当前对话历史（只读）
    /// </summary>
    public IReadOnlyList<ChatMessageContent> ConversationHistory => _conversationHistory.AsReadOnly();

    /// <summary>
    /// 当前股票代码
    /// </summary>
    public string CurrentStockCode => _currentStockCode;


    /// <summary>
    /// 是否正在处理请求
    /// </summary>
    public bool IsProcessing => _currentCancellationTokenSource != null && !_currentCancellationTokenSource.Token.IsCancellationRequested;

    #endregion

    #region 公共方法

    /// <summary>
    /// 发送消息并获取AI回复
    /// </summary>
    /// <param name="userMessage">用户消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>AI回复</returns>
    public async Task<ChatMessageContent> SendMessageAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("处理用户消息: {Message}", userMessage);

            // 不再进行硬编码的主题检测，让AI通过系统提示词自然处理

            // 添加用户消息到历史
            _conversationHistory.AddUserMessage(userMessage);

            // 检查并管理上下文窗口
            await ManageContextWindowAsync();

            // 不再进行硬编码的动态上下文注入

            // 创建取消令牌源
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentCancellationTokenSource = cts;

            try
            {
                // 调用AI服务获取回复
                var response = await _chatCompletionService.GetChatMessageContentAsync(
                    _conversationHistory,
                    executionSettings: new PromptExecutionSettings
                    {
                        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                    },
                    kernel: _kernel,
                    cancellationToken: cts.Token);

                // 添加AI回复到历史
                _conversationHistory.Add(response);

                _logger.LogInformation("AI回复成功");
                return response;
            }
            finally
            {
                _currentCancellationTokenSource = null;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("用户取消了对话请求");
            var cancelResponse = new ChatMessageContent(AuthorRole.Assistant, "对话已被取消。")
            {
                AuthorName = "市场分析助手"
            };
            return cancelResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理AI对话时发生错误");

            // 创建错误回复
            var errorResponse = new ChatMessageContent(AuthorRole.Assistant, "抱歉，我暂时无法回复您的问题，请稍后重试。")
            {
                AuthorName = "市场分析助手"
            };

            // 将错误回复也添加到历史中
            _conversationHistory.Add(errorResponse);

            return errorResponse;
        }
    }

    /// <summary>
    /// 发送消息并获取流式回复
    /// </summary>
    /// <param name="userMessage">用户消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>流式响应的异步枚举</returns>
    public async IAsyncEnumerable<StreamingChatMessageContent> SendMessageStreamAsync(
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始流式处理用户消息: {Message}", userMessage);

        // 不再进行硬编码的主题检测

        // 添加用户消息到历史
        _conversationHistory.AddUserMessage(userMessage);

        // 检查并管理上下文窗口
        await ManageContextWindowAsync();

        // 不再进行硬编码的动态上下文注入

        // 创建取消令牌源
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentCancellationTokenSource = cts;

        var completeResponse = new StringBuilder();

        // 获取流式响应
        var streamingEnumerable = _chatCompletionService.GetStreamingChatMessageContentsAsync(
            _conversationHistory,
            executionSettings: new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            },
            kernel: _kernel,
            cancellationToken: cts.Token);

        try
        {
            await foreach (var streamingResponse in streamingEnumerable)
            {
                // 累积完整响应
                if (streamingResponse.Content != null)
                {
                    completeResponse.Append(streamingResponse.Content);
                }

                // 触发流式响应事件
                StreamingResponse?.Invoke(this, new StreamingResponseEventArgs
                {
                    Content = streamingResponse.Content ?? string.Empty,
                    IsComplete = false
                });

                yield return streamingResponse;
            }
        }
        finally
        {
            _currentCancellationTokenSource = null;

            // 将完整响应添加到历史
            if (completeResponse.Length > 0)
            {
                var fullResponse = new ChatMessageContent(AuthorRole.Assistant, completeResponse.ToString())
                {
                    AuthorName = "市场分析助手"
                };
                _conversationHistory.Add(fullResponse);
            }

            // 触发完成事件
            StreamingResponse?.Invoke(this, new StreamingResponseEventArgs
            {
                Content = completeResponse.ToString(),
                IsComplete = true
            });

            _logger.LogInformation("流式AI回复完成");
        }
    }

    /// <summary>
    /// 取消当前对话请求
    /// </summary>
    public void CancelCurrentRequest()
    {
        _currentCancellationTokenSource?.Cancel();
        _logger.LogInformation("已取消当前对话请求");
    }

    /// <summary>
    /// 更新股票上下文
    /// </summary>
    /// <param name="stockCode">股票代码</param>
    public async Task UpdateStockContextAsync(string stockCode)
    {
        if (_currentStockCode == stockCode)
            return;

        _currentStockCode = stockCode;
        _logger.LogInformation("更新股票上下文: {StockCode}", stockCode);

        // 更新系统上下文
        await UpdateSystemContextAsync();

        // 添加上下文切换的系统消息
        if (!string.IsNullOrEmpty(stockCode))
        {
            var contextMessage = new ChatMessageContent(AuthorRole.System, $"当前分析股票已切换为: {stockCode}")
            {
                AuthorName = "系统"
            };
            _conversationHistory.Add(contextMessage);
        }
    }

    /// <summary>
    /// 清空对话历史
    /// </summary>
    public void ClearHistory()
    {
        _conversationHistory.Clear();
        InitializeSystemContext();
        _logger.LogInformation("已清空对话历史");
    }

    /// <summary>
    /// 添加系统消息
    /// </summary>
    /// <param name="message">系统消息内容</param>
    public void AddSystemMessage(string message)
    {
        var systemMessage = new ChatMessageContent(AuthorRole.System, message)
        {
            AuthorName = "系统"
        };
        _conversationHistory.Add(systemMessage);
    }


    /// <summary>
    /// 获取上下文统计信息
    /// </summary>
    /// <returns>上下文统计</returns>
    public ContextStatistics GetContextStatistics()
    {
        var messageCount = _conversationHistory.Count;
        var userMessageCount = _conversationHistory.Count(m => m.Role == AuthorRole.User);
        var assistantMessageCount = _conversationHistory.Count(m => m.Role == AuthorRole.Assistant);

        return new ContextStatistics
        {
            TotalMessages = messageCount,
            UserMessages = userMessageCount,
            AssistantMessages = assistantMessageCount,
            MessageUtilization = (double)messageCount / MaxContextMessages, // 基于消息数量的利用率
            CurrentStockCode = _currentStockCode
        };
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 初始化系统上下文
    /// </summary>
    private void InitializeSystemContext()
    {
        var systemPrompt = BuildSystemPrompt(_currentStockCode);
        _conversationHistory.AddSystemMessage(systemPrompt);
    }

    /// <summary>
    /// 更新系统上下文
    /// </summary>
    private async Task UpdateSystemContextAsync()
    {
        // 移除旧的系统消息（保留第一条基础系统消息）
        var systemMessages = _conversationHistory.Where(m => m.Role == AuthorRole.System).Skip(1).ToList();
        foreach (var msg in systemMessages)
        {
            _conversationHistory.Remove(msg);
        }

        // 添加新的股票上下文
        if (!string.IsNullOrEmpty(_currentStockCode))
        {
            var analysisContext = await GetAnalysisContextAsync(_conversationHistory, _currentStockCode);
            if (!string.IsNullOrEmpty(analysisContext))
            {
                _conversationHistory.AddSystemMessage($"当前股票分析上下文：\n{analysisContext}");
            }
        }
    }


    /// <summary>
    /// 管理上下文窗口
    /// </summary>
    private async Task ManageContextWindowAsync()
    {
        var currentMessageCount = _conversationHistory.Count;

        if (currentMessageCount <= MaxContextMessages)
            return;

        _logger.LogInformation("上下文窗口超限，当前消息数: {CurrentMessages}，开始压缩", currentMessageCount);

        // 保留系统消息和最近的消息
        var systemMessages = _conversationHistory.Where(m => m.Role == AuthorRole.System).ToList();
        var recentMessages = _conversationHistory
            .Where(m => m.Role != AuthorRole.System)
            .TakeLast(MinMessagesAfterCompression)
            .ToList();

        var originalCount = _conversationHistory.Count;

        // 清空历史并重新构建
        _conversationHistory.Clear();

        // 添加系统消息
        foreach (var systemMessage in systemMessages)
        {
            _conversationHistory.Add(systemMessage);
        }

        // 添加压缩摘要
        if (recentMessages.Count > 0)
        {
            var compressionSummary = await CreateCompressionSummaryAsync(recentMessages);
            if (!string.IsNullOrEmpty(compressionSummary))
            {
                _conversationHistory.AddSystemMessage($"之前对话摘要：\n{compressionSummary}");
            }
        }

        // 添加最近的消息
        foreach (var message in recentMessages)
        {
            _conversationHistory.Add(message);
        }

        var newCount = _conversationHistory.Count;

        _logger.LogInformation("上下文压缩完成，消息数: {OriginalCount} -> {NewCount}",
            originalCount, newCount);

        // 触发压缩事件
        ContextCompressed?.Invoke(this, new ContextCompressionEventArgs
        {
            OriginalMessageCount = originalCount,
            NewMessageCount = newCount,
            CompressionRatio = (double)newCount / originalCount
        });
    }

    /// <summary>
    /// 创建压缩摘要
    /// </summary>
    /// <param name="messages">要压缩的消息</param>
    /// <returns>压缩摘要</returns>
    private async Task<string> CreateCompressionSummaryAsync(IEnumerable<ChatMessageContent> messages)
    {
        try
        {
            var messageTexts = messages
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => $"{m.Role}: {m.Content}")
                .ToList();

            if (messageTexts.Count == 0)
                return string.Empty;

            var combinedText = string.Join("\n\n", messageTexts);

            // 创建临时对话历史用于压缩
            var compressionHistory = new ChatHistory();
            compressionHistory.AddSystemMessage("请将以下对话内容压缩成简洁的摘要，保留关键信息和上下文：");
            compressionHistory.AddUserMessage(combinedText);

            var summary = await _chatCompletionService.GetChatMessageContentAsync(compressionHistory);
            return summary.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建压缩摘要失败");
            return "之前的对话已被压缩以节省空间。";
        }
    }


    /// <summary>
    /// 构建系统提示词
    /// </summary>
    /// <param name="stockCode">股票代码</param>
    /// <returns>系统提示词</returns>
    private static string BuildSystemPrompt(string stockCode)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("你是一个专业的股票市场分析助手，具备以下能力：");
        prompt.AppendLine("1. 提供专业的股票分析和投资建议");
        prompt.AppendLine("2. 解答用户关于股票市场的各种问题");
        prompt.AppendLine("3. 基于技术分析、基本面分析等多维度提供见解");
        prompt.AppendLine("4. 保持客观、专业的态度，提醒投资风险");
        prompt.AppendLine();
        prompt.AppendLine("回复要求：");
        prompt.AppendLine("- 语言简洁明了，避免过于技术化的术语");
        prompt.AppendLine("- 提供具体的数据和分析依据");
        prompt.AppendLine("- 始终提醒用户投资有风险，需要谨慎决策");

        if (!string.IsNullOrEmpty(stockCode))
        {
            prompt.AppendLine();
            prompt.AppendLine($"**当前分析焦点：{stockCode}**");
            prompt.AppendLine();
            prompt.AppendLine("重要约束：");
            prompt.AppendLine($"- 你的专业领域是股票 {stockCode} 的分析，请将所有回复都围绕这只股票展开");
            prompt.AppendLine("- 如果用户询问与该股票无关的问题（如天气、娱乐、其他股票等），请礼貌地引导用户回到股票分析主题");
            prompt.AppendLine($"- 引导示例：\"我专注于为您分析 {stockCode}，让我们聊聊这只股票的[技术面/基本面/市场表现]如何？\"");
            prompt.AppendLine("- 根据用户问题的类型，自动调整分析角度：");
            prompt.AppendLine("  * 技术分析问题 → 重点分析图表形态、技术指标、价格趋势");
            prompt.AppendLine("  * 基本面问题 → 重点分析财务数据、业务模式、行业地位");
            prompt.AppendLine("  * 风险相关问题 → 特别强调风险提示和风险管理建议");
        }
        else
        {
            prompt.AppendLine();
            prompt.AppendLine("- 当用户指定具体股票时，请专注分析该股票");
            prompt.AppendLine("- 根据问题类型智能调整分析视角和重点");
        }

        return prompt.ToString();
    }

    /// <summary>
    /// 获取股票分析上下文
    /// </summary>
    /// <returns>分析上下文摘要</returns>
    private Task<string> GetAnalysisContextAsync(ChatHistory history, string stockCode)
    {
        try
        {
            if (history.Any())
            {
                // 提取分析师的关键观点作为上下文
                var analysisContext = ExtractAnalysisContext(history, stockCode);
                return Task.FromResult(analysisContext);
            }

            // 如果没有分析历史，返回基础上下文
            return Task.FromResult($"当前股票：{stockCode}。您可以询问关于该股票的技术分析、基本面分析、市场情绪等问题，我会为您提供专业的分析。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取股票分析上下文失败，股票代码: {StockCode}", stockCode);
            return Task.FromResult(string.Empty);
        }
    }

    /// <summary>
    /// 从分析历史中提取关键上下文
    /// </summary>
    /// <param name="analysisHistory">分析历史</param>
    /// <param name="stockCode">股票代码</param>
    /// <returns>提取的上下文摘要</returns>
    private string ExtractAnalysisContext(ChatHistory analysisHistory, string stockCode)
    {
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine($"当前分析股票：{stockCode}");
        contextBuilder.AppendLine("最近的分析观点摘要：");

        // 获取最近的分析师观点（限制长度避免上下文过长）
        var recentMessages = analysisHistory
            .Where(m => m.Role == AuthorRole.Assistant && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(3) // 只取最近3条分析师观点
            .ToList();

        foreach (var message in recentMessages)
        {
            var authorName = message.AuthorName ?? "分析师";
            var content = message.Content?.Length > 200
                ? message.Content.Substring(0, 200) + "..."
                : message.Content;

            contextBuilder.AppendLine($"- {authorName}: {content}");
        }

        return contextBuilder.ToString();
    }

    #endregion
}

/// <summary>
/// 流式响应事件参数
/// </summary>
public class StreamingResponseEventArgs : EventArgs
{
    /// <summary>
    /// 响应内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 是否完成
    /// </summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// 上下文压缩事件参数
/// </summary>
public class ContextCompressionEventArgs : EventArgs
{
    /// <summary>
    /// 原始消息数量
    /// </summary>
    public int OriginalMessageCount { get; set; }

    /// <summary>
    /// 压缩后消息数量
    /// </summary>
    public int NewMessageCount { get; set; }

    /// <summary>
    /// 压缩比率（基于消息数量）
    /// </summary>
    public double CompressionRatio { get; set; }
}


/// <summary>
/// 上下文统计信息
/// </summary>
public class ContextStatistics
{
    /// <summary>
    /// 总消息数
    /// </summary>
    public int TotalMessages { get; set; }

    /// <summary>
    /// 用户消息数
    /// </summary>
    public int UserMessages { get; set; }

    /// <summary>
    /// 助手消息数
    /// </summary>
    public int AssistantMessages { get; set; }

    /// <summary>
    /// 消息利用率（基于最大消息数量）
    /// </summary>
    public double MessageUtilization { get; set; }

    /// <summary>
    /// 当前股票代码
    /// </summary>
    public string CurrentStockCode { get; set; } = string.Empty;
}