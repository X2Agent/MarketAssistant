using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 协调分析师 Executor（优化版：使用框架原生结构化输出）
/// 负责汇总各分析师的分析并生成最终报告
/// 使用 AIAgent 支持工具调用 + 结构化输出
/// </summary>
public sealed partial class CoordinatorExecutor : Executor
{
    private readonly AIAgent _coordinatorAgent;
    private readonly ILogger<CoordinatorExecutor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // 添加枚举转换器，支持字符串格式（使用原始枚举名称，如 "Hold"）
            new JsonStringEnumConverter()
        }
    };

    /// <summary>首次调用 + 1 次修复重试</summary>
    private const int MaxAttempts = 2;

    /// <summary>校验错误中 null 违规的标记片段，用于区分硬伤与可降级的值违规</summary>
    private const string NullViolationMarker = "值不能为空";

    public CoordinatorExecutor(
        AIAgent coordinatorAgent,
        ILogger<CoordinatorExecutor> logger)
        : base("Coordinator")
    {
        _coordinatorAgent = coordinatorAgent ?? throw new ArgumentNullException(nameof(coordinatorAgent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [MessageHandler]
    private async ValueTask<MarketAnalysisReport> HandleAsync(
        List<ChatMessage> analystMessages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CoordinatorExecutor.HandleAsync 被调用，收到 {Count} 条消息", analystMessages?.Count ?? 0);

        ArgumentNullException.ThrowIfNull(analystMessages);

        if (analystMessages.Count == 0)
        {
            throw new ArgumentException("没有分析师数据", nameof(analystMessages));
        }

        // 从工作流状态读取分析标的代码
        var assetSymbol = await context.ReadStateAsync<string>(WorkflowStateKeys.AssetSymbol, WorkflowStateKeys.Scope, cancellationToken);

        if (string.IsNullOrWhiteSpace(assetSymbol))
        {
            throw new InvalidOperationException("无法从工作流状态中获取标的代码");
        }

        _logger.LogInformation(
            "协调分析师开始生成最终报告，标的: {AssetSymbol}, 分析师数量: {Count}",
            assetSymbol,
            analystMessages.Count);

        try
        {
            LogMessageDiagnostics("协调分析师收到上游消息", analystMessages);

            // 过滤消息：移除包含工具调用(FunctionCallContent)和结果(FunctionResultContent)的消息
            // 这样可以显著减少 Token 消耗，并避免 Coordinator 被中间过程干扰
            var filteredMessages = analystMessages
                .Where(m => !m.Contents.Any(c => c is FunctionCallContent or FunctionResultContent))
                .ToList();

            _logger.LogInformation(
                "协调分析师输入过滤完成，原始消息: {OriginalCount}，保留消息: {FilteredCount}",
                analystMessages.Count,
                filteredMessages.Count);
            LogMessageDiagnostics("协调分析师过滤后输入", filteredMessages);

            // 所有分析师均无文本输出（仅产生工具调用）时，无法生成有意义的综合报告
            if (filteredMessages.Count == 0)
            {
                throw new InvalidOperationException(
                    "所有分析师均未产生文本结论，无法生成综合报告");
            }

            // 构建聊天消息列表
            var messages = new List<ChatMessage>(filteredMessages)
            {
                // 添加用户请求：生成综合报告
                new ChatMessage(
                ChatRole.User,
                $"请基于以上所有分析师的专业意见，为标的 {assetSymbol} 生成一份综合分析报告。")
            };

            _logger.LogInformation(
                "调用协调分析师，输入消息: {MessageCount}，输入文本总长度: {TextLength}",
                messages.Count,
                messages.Sum(message => message.Text?.Length ?? 0));

            // 修复重试循环：解析/校验失败时将错误清单反馈给模型（同一对话上下文）重试，
            // 仍失败则分层降级（null 硬伤抛异常，值违规钳制后接受），避免作废整个分析流程。
            CoordinatorResult? coordinatorResult = null;
            ChatMessage? coordinatorMessage = null;
            var validationErrors = new List<string>();
            var parseFailed = false;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // 使用带结构化输出的 ChatClientAgent 运行。
                // session: null — 无状态一次性调用，无需会话累积。
                var startedAt = Stopwatch.GetTimestamp();
                var agentResponse = await _coordinatorAgent.RunAsync(
                    messages,
                    session: null,
                    options: null,
                    cancellationToken);
                var elapsed = Stopwatch.GetElapsedTime(startedAt);

                _logger.LogInformation(
                    "协调分析师调用完成（第 {Attempt}/{MaxAttempts} 次），耗时: {ElapsedMs} ms，响应消息: {MessageCount}，聚合文本长度: {TextLength}",
                    attempt,
                    MaxAttempts,
                    elapsed.TotalMilliseconds,
                    agentResponse.Messages.Count,
                    agentResponse.Text?.Length ?? 0);
                LogMessageDiagnostics("协调分析师原始响应", agentResponse.Messages);

                // 提取协调分析师的回复（最后一条 Assistant 消息）
                coordinatorMessage = agentResponse.Messages
                    .LastOrDefault(m => m.Role == ChatRole.Assistant);

                if (coordinatorMessage == null)
                {
                    throw new InvalidOperationException("协调分析师未能生成报告");
                }

                // 从协调分析师的回复文本中反序列化结构化结果
                // 部分兼容模型即使启用 JsonObject 仍可能在 JSON 前后输出多余文本，
                // 使用 LlmJsonExtractor 进行多层兜底解析（直接解析 → 剥离 markdown → Utf8JsonReader 精确定位）
                var rawText = coordinatorMessage.Text ?? string.Empty;
                _logger.LogInformation(
                    "准备解析协调分析师最后一条 Assistant 消息，文本长度: {TextLength}，Content 类型: [{ContentTypes}]",
                    rawText.Length,
                    string.Join(", ", coordinatorMessage.Contents.Select(content => content.GetType().Name)));
                _logger.LogDebug(
                    "协调分析师最后一条 Assistant 消息预览: {Preview}",
                    CreatePreview(rawText));

                // 解析与校验错误统一收集，供修复反馈与最终降级决策使用；
                // 每轮重置，确保状态仅来自最近一次模型输出
                coordinatorResult = null;
                parseFailed = false;
                validationErrors = [];

                try
                {
                    coordinatorResult = LlmJsonExtractor.Deserialize<CoordinatorResult>(rawText, JsonOptions);
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx,
                        "协调分析师 JSON 解析失败，原始文本长度: {TextLength}，前 500 字符: {Preview}",
                        rawText.Length,
                        CreatePreview(rawText));
                    parseFailed = true;
                    validationErrors.Add($"JSON 解析失败: {jsonEx.Message}");
                }

                if (coordinatorResult == null && !parseFailed)
                {
                    _logger.LogError(
                        "协调分析师结构化解析结果为空，最后一条 Assistant 文本长度: {TextLength}，响应总消息数: {MessageCount}",
                        rawText.Length,
                        agentResponse.Messages.Count);
                    parseFailed = true;
                    validationErrors.Add("结构化输出为空");
                }

                if (coordinatorResult != null)
                {
                    validationErrors.AddRange(StructuredOutputValidator.Validate(coordinatorResult));
                }

                if (validationErrors.Count == 0)
                {
                    break;
                }

                if (attempt < MaxAttempts)
                {
                    _logger.LogWarning(
                        "协调分析师第 {Attempt}/{MaxAttempts} 次返回未通过校验，发起修复重试: {Errors}",
                        attempt,
                        MaxAttempts,
                        string.Join("; ", validationErrors));
                    messages = [.. messages, coordinatorMessage,
                        new ChatMessage(ChatRole.User, BuildRepairFeedback(rawText, validationErrors, parseFailed))];
                }
            }

            if (validationErrors.Count > 0)
            {
                // 解析失败或 null 类硬伤：结果不可用，或下游（卡片解析/ViewModel）假设属性非空，保持失败
                if (coordinatorResult == null ||
                    validationErrors.Any(error => error.Contains(NullViolationMarker, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"协调分析师返回的数据不符合约束: {string.Join("; ", validationErrors)}");
                }

                // 降级接受：值违规（越界/长度/数量/枚举）不作废整个分析流程，钳制数值字段后继续
                _logger.LogWarning(
                    "协调分析师结果经 {MaxAttempts} 次调用仍未通过校验，降级接受: {Errors}",
                    MaxAttempts,
                    string.Join("; ", validationErrors));
                ClampNumericScores(coordinatorResult);
            }

            _logger.LogInformation(
                "成功获取协调分析师的结构化数据，综合评分: {Score}，最终评级: {Rating}",
                coordinatorResult!.OverallScore,
                coordinatorResult.InvestmentRating);

            // 创建最终报告
            // 仅保存过滤后的分析师消息（不含工具调用细节），避免归档时序列化 FunctionResultContent 失败
            var finalReport = new MarketAnalysisReport
            {
                AssetSymbol = assetSymbol,
                AnalystMessages = new List<ChatMessage>(filteredMessages)
                {
                    coordinatorMessage!
                },
                CoordinatorResult = coordinatorResult,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("协调分析师已完成最终报告生成，标的: {AssetSymbol}",
                assetSymbol);

            // 显式将报告推入工作流输出队列。
            // WithOutputFrom 的 auto-yield 在 RunStreamingAsync（流式执行）路径下
            // 存在时序缺陷：WatchStreamAsync 的 IAsyncEnumerable 在 auto-yield 入队前
            // 已结束迭代，导致 WorkflowOutputEvent 丢失。显式调用 YieldOutputAsync
            // 确保事件在 Executor 返回前入队，对非流式路径无副作用。
            await context.YieldOutputAsync(finalReport, cancellationToken);

            return finalReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "协调分析师生成报告时发生错误，标的: {AssetSymbol}",
                await context.ReadStateAsync<string>(WorkflowStateKeys.AssetSymbol, WorkflowStateKeys.Scope, cancellationToken) ?? "未知");
            throw;
        }
    }

    /// <summary>
    /// 构建修复反馈消息：将校验错误连同上一轮输出反馈给模型，要求其仅修正问题并重新输出完整 JSON。
    /// </summary>
    private static string BuildRepairFeedback(string rawText, IReadOnlyList<string> errors, bool parseFailed)
    {
        if (parseFailed)
        {
            return $"""
                你上一轮返回的内容无法解析为 JSON：{string.Join("; ", errors)}

                请重新输出一个符合 JSON Schema 的合法 JSON 对象，不要输出任何解释文字或 Markdown 代码块。

                你上一轮返回的内容：
                {rawText}
                """;
        }

        var errorList = string.Join(Environment.NewLine, errors.Select(error => $"- {error}"));

        return $"""
            你上一轮返回的 JSON 未通过约束校验，请修正后重新输出。

            要求：
            1. 仅修正下方校验错误对应的字段值，其余字段保持原值不变
            2. 仅输出修正后的完整 JSON 对象，不要输出任何解释文字、思考过程或 Markdown 代码块
            3. 所有字段约束以先前提供的 JSON Schema 为准

            校验错误：
            {errorList}

            你上一轮返回的内容：
            {rawText}
            """;
    }

    /// <summary>
    /// 降级路径下将数值评分字段钳制到约束范围，避免越界数值误导 UI 展示与 AI 交易决策上下文。
    /// </summary>
    private static void ClampNumericScores(CoordinatorResult result)
    {
        result.OverallScore = Math.Clamp(result.OverallScore, 1, 10);
        result.ConfidencePercentage = Math.Clamp(result.ConfidencePercentage, 0, 100);
        result.DimensionScores.Fundamental = Math.Clamp(result.DimensionScores.Fundamental, 1, 10);
        result.DimensionScores.Technical = Math.Clamp(result.DimensionScores.Technical, 1, 10);
        result.DimensionScores.Financial = Math.Clamp(result.DimensionScores.Financial, 1, 10);
        result.DimensionScores.Sentiment = Math.Clamp(result.DimensionScores.Sentiment, 1, 10);
        result.DimensionScores.News = Math.Clamp(result.DimensionScores.News, 1, 10);
    }

    private void LogMessageDiagnostics(string stage, IEnumerable<ChatMessage> messages)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        for (var index = 0; index < messageList.Count; index++)
        {
            var message = messageList[index];
            var text = message.Text ?? string.Empty;
            var contentTypes = string.Join(", ", message.Contents.Select(content => content.GetType().Name));
            _logger.LogInformation(
                "{Stage} [{Index}/{Count}] Role: {Role}, Author: {Author}, TextLength: {TextLength}, ContentTypes: [{ContentTypes}]",
                stage,
                index + 1,
                messageList.Count,
                message.Role,
                message.AuthorName ?? "null",
                text.Length,
                contentTypes);
            _logger.LogDebug(
                "{Stage} [{Index}/{Count}] 文本预览: {Preview}",
                stage,
                index + 1,
                messageList.Count,
                CreatePreview(text));
        }
    }

    private static string CreatePreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "<empty>";

        var normalized = text.ReplaceLineEndings(" ");
        return normalized.Length > 500 ? normalized[..500] : normalized;
    }

}
