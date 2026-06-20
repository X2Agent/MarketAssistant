using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
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

    // 针对瞬态 LLM 故障的重试管道：最多重试 2 次，指数退避 + 抖动
    private static readonly ResiliencePipeline _llmRetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(2),
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
        })
        .Build();

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
        IAnalystAgentFactory analystAgentFactory,
        ILogger<CoordinatorExecutor> logger)
        : base("Coordinator")
    {
        ArgumentNullException.ThrowIfNull(analystAgentFactory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 在构造函数中创建 Agent（确保 tools 配置正确）
        // 使用非泛型方法：CreateAnalyst 返回的是中间件包装后的 AIAgent，无法强制转换为具体类型
        _coordinatorAgent = analystAgentFactory.CreateAnalyst(typeof(CoordinatorAnalystAgent));

        _logger.LogInformation("协调分析师 Agent 已创建（支持工具调用 + 结构化输出）");
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

            // 构建聊天消息列表
            var messages = new List<ChatMessage>(filteredMessages)
            {
                // 添加用户请求：生成综合报告
                new ChatMessage(
                ChatRole.User,
                $"请基于以上所有分析师的专业意见，为标的 {assetSymbol} 生成一份综合分析报告。")
            };

            // 使用带结构化输出的 ChatClientAgent 运行（包含重试）
            // session: null — 无状态一次性调用，无需会话累积
            var agentResponse = await _llmRetryPipeline.ExecuteAsync(
                async ct => await _coordinatorAgent.RunAsync(
                    messages,
                    session: null,
                    options: null,
                    ct),
                cancellationToken);

            // 提取协调分析师的回复（最后一条 Assistant 消息）
            var coordinatorMessage = agentResponse.Messages
                .LastOrDefault(m => m.Role == ChatRole.Assistant);

            if (coordinatorMessage == null)
            {
                throw new InvalidOperationException("协调分析师未能生成报告");
            }

            // 从协调分析师的回复文本中反序列化结构化结果
            // ChatResponseFormat.ForJsonSchema 保证输出为纯 JSON，无需正则剥离 markdown 代码块
            var rawText = coordinatorMessage.Text ?? string.Empty;

            CoordinatorResult? coordinatorResult;
            try
            {
                coordinatorResult = JsonSerializer.Deserialize<CoordinatorResult>(rawText, JsonOptions);
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

            _logger.LogInformation(
                "成功获取协调分析师的结构化数据，综合评分: {Score}，最终评级: {Rating}",
                coordinatorResult.OverallScore,
                coordinatorResult.InvestmentRating);

            // 创建最终报告
            var finalReport = new MarketAnalysisReport
            {
                AssetSymbol = assetSymbol,
                AnalystMessages = new List<ChatMessage>(analystMessages)
                {
                    coordinatorMessage
                },
                CoordinatorResult = coordinatorResult,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("协调分析师已完成最终报告生成，标的: {AssetSymbol}",
                assetSymbol);

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
