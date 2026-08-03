using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
            // 过滤消息：移除包含工具调用(FunctionCallContent)和结果(FunctionResultContent)的消息
            // 这样可以显著减少 Token 消耗，并避免 Coordinator 被中间过程干扰
            var filteredMessages = analystMessages
                .Where(m => !m.Contents.Any(c => c is FunctionCallContent or FunctionResultContent))
                .ToList();

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

            // 使用带结构化输出的 ChatClientAgent 运行
            // 重试由 ResilientChatClient 装饰器统一提供，此处无需额外重试管道
            // session: null — 无状态一次性调用，无需会话累积
            var agentResponse = await _coordinatorAgent.RunAsync(
                messages,
                session: null,
                options: null,
                cancellationToken);

            // 提取协调分析师的回复（最后一条 Assistant 消息）
            var coordinatorMessage = agentResponse.Messages
                .LastOrDefault(m => m.Role == ChatRole.Assistant);

            if (coordinatorMessage == null)
            {
                throw new InvalidOperationException("协调分析师未能生成报告");
            }

            // 从协调分析师的回复文本中反序列化结构化结果
            // 部分兼容模型即使启用 JsonObject 仍可能在 JSON 前后输出多余文本，
            // 使用 LlmJsonExtractor 进行多层兜底解析（直接解析 → 剥离 markdown → Utf8JsonReader 精确定位）
            var rawText = coordinatorMessage.Text ?? string.Empty;

            CoordinatorResult? coordinatorResult;
            try
            {
                coordinatorResult = LlmJsonExtractor.Deserialize<CoordinatorResult>(rawText, JsonOptions);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx,
                    "协调分析师 JSON 解析失败，原始文本前 500 字符: {Preview}",
                    rawText.Length > 500 ? rawText[..500] : rawText);
                throw new InvalidOperationException(
                    $"协调分析师返回的数据无法解析为结构化结果: {jsonEx.Message}", jsonEx);
            }

            if (coordinatorResult == null)
            {
                throw new InvalidOperationException("协调分析师未能返回结构化数据");
            }

            var validationErrors = StructuredOutputValidator.Validate(coordinatorResult);
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"协调分析师返回的数据不符合约束: {string.Join("; ", validationErrors)}");
            }

            _logger.LogInformation(
                "成功获取协调分析师的结构化数据，综合评分: {Score}，最终评级: {Rating}",
                coordinatorResult.OverallScore,
                coordinatorResult.InvestmentRating);

            // 创建最终报告
            // 仅保存过滤后的分析师消息（不含工具调用细节），避免归档时序列化 FunctionResultContent 失败
            var finalReport = new MarketAnalysisReport
            {
                AssetSymbol = assetSymbol,
                AnalystMessages = new List<ChatMessage>(filteredMessages)
                {
                    coordinatorMessage
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

}
