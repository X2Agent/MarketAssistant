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
    private const int MinMessagesAfterCompression = 20;

    /// <summary>
    /// 压缩时保留的重要消息数量
    /// </summary>
    private const int ImportantMessagesCount = 10;

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
    /// 系统消息缓存，避免重复生成相同内容
    /// </summary>
    private readonly Dictionary<string, ChatMessageContent> _systemMessageCache = new();

    /// <summary>
    /// 取消令牌源
    /// </summary>
    private CancellationTokenSource? _currentCancellationTokenSource;

    #endregion

    #region 构造函数

    public MarketChatAgent(
        ILogger<MarketChatAgent> logger,
        Kernel kernel,
        MarketAnalysisAgent? analysisAgent = null)
    {
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
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

        var previousStockCode = _currentStockCode;
        _currentStockCode = stockCode;
        _logger.LogInformation("更新股票上下文: {PreviousStock} -> {NewStock}", previousStockCode, stockCode);

        // 更新系统上下文
        await UpdateSystemContextAsync();

        // 只在有实际对话历史时才添加切换提示，避免空对话时的冗余消息
        var hasUserMessages = _conversationHistory.Any(m => m.Role == AuthorRole.User);
        
        if (hasUserMessages && !string.IsNullOrEmpty(stockCode))
        {
            var switchKey = "stock_switch_notification";
            var switchMessage = string.IsNullOrEmpty(previousStockCode) 
                ? $"开始分析股票: {stockCode}"
                : $"分析焦点已从 {previousStockCode} 切换到 {stockCode}";
            
            // 移除之前的切换通知
            RemoveSystemMessageByKey(switchKey);
            
            // 添加新的切换通知
            var contextMessage = new ChatMessageContent(AuthorRole.System, switchMessage)
            {
                AuthorName = "系统"
            };
            
            _systemMessageCache[switchKey] = contextMessage;
            _conversationHistory.Add(contextMessage);
        }
    }

    /// <summary>
    /// 清空对话历史
    /// </summary>
    public void ClearHistory()
    {
        _conversationHistory.Clear();
        _systemMessageCache.Clear();
        InitializeSystemContext();
        _logger.LogInformation("已清空对话历史和系统消息缓存");
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
        // 生成新的股票上下文消息键
        var contextKey = $"stock_context_{_currentStockCode}";
        
        if (!string.IsNullOrEmpty(_currentStockCode))
        {
            var analysisContext = await GetAnalysisContextAsync(_conversationHistory, _currentStockCode);
            if (!string.IsNullOrEmpty(analysisContext))
            {
                var newContextContent = $"当前股票分析上下文：\n{analysisContext}";
                
                // 检查是否需要更新（内容是否有变化）
                if (!_systemMessageCache.ContainsKey(contextKey) || 
                    _systemMessageCache[contextKey].Content != newContextContent)
                {
                    // 移除旧的股票上下文消息
                    RemoveSystemMessageByKey(contextKey);
                    
                    // 创建并缓存新的上下文消息
                    var contextMessage = new ChatMessageContent(AuthorRole.System, newContextContent)
                    {
                        AuthorName = "系统"
                    };
                    
                    _systemMessageCache[contextKey] = contextMessage;
                    _conversationHistory.Add(contextMessage);
                }
            }
        }
        else
        {
            // 如果没有股票代码，移除相关的上下文消息
            RemoveSystemMessageByKey(contextKey);
        }
    }

    /// <summary>
    /// 移除指定键的系统消息
    /// </summary>
    /// <param name="key">消息键</param>
    private void RemoveSystemMessageByKey(string key)
    {
        if (_systemMessageCache.ContainsKey(key))
        {
            var messageToRemove = _systemMessageCache[key];
            _conversationHistory.Remove(messageToRemove);
            _systemMessageCache.Remove(key);
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

        var originalCount = _conversationHistory.Count;

        // 保留系统消息
        var systemMessages = _conversationHistory.Where(m => m.Role == AuthorRole.System).ToList();
        var nonSystemMessages = _conversationHistory.Where(m => m.Role != AuthorRole.System).ToList();

        // 智能选择要保留的消息
        var selectedMessages = await SelectMessagesForRetentionAsync(nonSystemMessages);

        // 获取需要压缩的消息（除了选中保留的消息）
        var messagesToCompress = nonSystemMessages.Except(selectedMessages).ToList();

        // 清空历史并重新构建
        _conversationHistory.Clear();

        // 添加系统消息
        foreach (var systemMessage in systemMessages)
        {
            _conversationHistory.Add(systemMessage);
        }

        // 添加压缩摘要（如果有需要压缩的消息）
        if (messagesToCompress.Count > 0)
        {
            try
            {
                var compressionSummary = await CreateCompressionSummaryAsync(messagesToCompress);
                if (!string.IsNullOrEmpty(compressionSummary))
                {
                    _conversationHistory.AddSystemMessage($"之前对话摘要（压缩了 {messagesToCompress.Count} 条消息）：\n{compressionSummary}");
                }
                else
                {
                    // 如果压缩失败，添加基本的统计信息作为备用
                    var userMsgCount = messagesToCompress.Count(m => m.Role == AuthorRole.User);
                    var assistantMsgCount = messagesToCompress.Count(m => m.Role == AuthorRole.Assistant);
                    var fallbackSummary = $"之前进行了 {userMsgCount} 轮用户询问和 {assistantMsgCount} 次AI回复，主要围绕股票 {_currentStockCode} 的相关分析。";
                    _conversationHistory.AddSystemMessage(fallbackSummary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "压缩摘要生成失败，使用备用方案");
                var fallbackSummary = $"之前的 {messagesToCompress.Count} 条对话已压缩，主要内容涉及股票分析和投资讨论。";
                _conversationHistory.AddSystemMessage(fallbackSummary);
            }
        }

        // 添加保留的消息（按时间顺序）
        var orderedSelectedMessages = selectedMessages
            .OrderBy(m => nonSystemMessages.IndexOf(m))
            .ToList();
        
        foreach (var message in orderedSelectedMessages)
        {
            _conversationHistory.Add(message);
        }

        var newCount = _conversationHistory.Count;

        _logger.LogInformation("上下文压缩完成，消息数: {OriginalCount} -> {NewCount}，保留重要消息: {RetainedCount}",
            originalCount, newCount, selectedMessages.Count);

        // 触发压缩事件
        ContextCompressed?.Invoke(this, new ContextCompressionEventArgs
        {
            OriginalMessageCount = originalCount,
            NewMessageCount = newCount,
            CompressionRatio = (double)newCount / originalCount
        });
    }

    /// <summary>
    /// 智能选择要保留的消息
    /// </summary>
    /// <param name="messages">所有非系统消息</param>
    /// <returns>选中保留的消息列表</returns>
    private async Task<List<ChatMessageContent>> SelectMessagesForRetentionAsync(List<ChatMessageContent> messages)
    {
        if (messages.Count <= MinMessagesAfterCompression)
            return messages;

        var selectedMessages = new List<ChatMessageContent>();

        // 1. 始终保留最近的消息（保证对话连续性）
        var recentMessages = messages.TakeLast(MinMessagesAfterCompression / 2).ToList();
        selectedMessages.AddRange(recentMessages);

        // 2. 从剩余消息中选择重要消息
        var remainingMessages = messages.Except(recentMessages).ToList();
        var importantMessages = await SelectImportantMessagesAsync(remainingMessages, ImportantMessagesCount);
        selectedMessages.AddRange(importantMessages);

        return selectedMessages.Distinct().ToList();
    }

    /// <summary>
    /// 选择重要消息
    /// </summary>
    /// <param name="messages">候选消息列表</param>
    /// <param name="count">要选择的数量</param>
    /// <returns>重要消息列表</returns>
    private Task<List<ChatMessageContent>> SelectImportantMessagesAsync(List<ChatMessageContent> messages, int count)
    {
        if (messages.Count <= count)
            return Task.FromResult(messages);

        // 按重要性评分排序
        var scoredMessages = messages.Select(m => new
        {
            Message = m,
            Score = CalculateMessageImportanceScore(m)
        }).OrderByDescending(x => x.Score).ToList();

        var result = scoredMessages.Take(count).Select(x => x.Message).ToList();
        return Task.FromResult(result);
    }

    /// <summary>
    /// 计算消息重要性得分
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>重要性得分</returns>
    private double CalculateMessageImportanceScore(ChatMessageContent message)
    {
        if (string.IsNullOrWhiteSpace(message.Content))
            return 0;

        var content = message.Content.ToLowerInvariant();
        double score = 0;

        // 基础得分：消息长度（较长的消息通常包含更多信息）
        score += Math.Min(content.Length / 100.0, 5.0);

        // 股票相关关键词加分
        var stockKeywords = new[] { "股票", "股价", "涨跌", "市盈率", "市净率", "成交量", "技术分析", "基本面", "财务", "收益", "风险", "投资", "分析", "建议", "趋势", "指标" };
        score += stockKeywords.Count(keyword => content.Contains(keyword)) * 2.0;

        // 当前股票代码相关加分
        if (!string.IsNullOrEmpty(_currentStockCode) && content.Contains(_currentStockCode.ToLowerInvariant()))
        {
            score += 5.0;
        }

        // 数字和数据加分（通常包含重要的分析数据）
        var numberMatches = System.Text.RegularExpressions.Regex.Matches(content, @"\d+\.?\d*%?");
        score += Math.Min(numberMatches.Count * 0.5, 3.0);

        // 问句和分析结论加分
        if (content.Contains("?") || content.Contains("？"))
            score += 1.0;
        
        if (content.Contains("结论") || content.Contains("建议") || content.Contains("总结"))
            score += 2.0;

        // 用户消息相对于AI回复有更高权重（用户问题通常很重要）
        if (message.Role == AuthorRole.User)
            score *= 1.5;

        return score;
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

            // 限制输入长度，避免压缩请求过长
            var combinedText = string.Join("\n\n", messageTexts);
            if (combinedText.Length > 4000) // 限制输入长度
            {
                combinedText = combinedText.Substring(0, 4000) + "...（内容已截断）";
            }

            // 创建临时对话历史用于压缩，使用更专业的提示词
            var compressionHistory = new ChatHistory();
            var compressionPrompt = $"请将以下股票分析对话内容压缩成简洁的摘要。重点保留：\n" +
                                   $"1. 涉及的股票代码和名称\n" +
                                   $"2. 关键的分析结论和数据\n" +
                                   $"3. 重要的投资建议或风险提示\n" +
                                   $"4. 用户关心的核心问题\n" +
                                   $"请用3-5句话概括，保持专业性：";
            
            compressionHistory.AddSystemMessage(compressionPrompt);
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
        prompt.AppendLine("4. 主动使用可用的分析工具获取实时数据");
        prompt.AppendLine("5. 保持客观、专业的态度，提醒投资风险");
        prompt.AppendLine();
        
        prompt.AppendLine("工具使用指导：");
        prompt.AppendLine("- 当需要股票基本信息时，优先使用股票基础信息插件");
        prompt.AppendLine("- 当需要财务数据时，使用财务分析插件获取准确数据");
        prompt.AppendLine("- 当需要技术指标时，使用技术分析插件计算指标");
        prompt.AppendLine("- 当需要最新新闻时，使用新闻搜索插件获取资讯");
        prompt.AppendLine("- 当需要筛选股票时，使用股票筛选插件");
        prompt.AppendLine();
        
        prompt.AppendLine("回复格式要求：");
        prompt.AppendLine("- 使用结构化格式：【核心观点】、【数据支撑】、【技术分析】、【风险提示】");
        prompt.AppendLine("- 语言简洁明了，避免过于技术化的术语");
        prompt.AppendLine("- 提供具体的数据和分析依据");
        prompt.AppendLine("- 重要数据用**粗体**标注");
        prompt.AppendLine("- 始终在结尾提醒投资风险");

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