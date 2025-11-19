using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Factories;
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
public sealed class CoordinatorExecutor : Executor<List<ChatMessage>, MarketAnalysisReport>
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
        IAnalystAgentFactory analystAgentFactory,
        ILogger<CoordinatorExecutor> logger)
        : base("Coordinator")
    {
        ArgumentNullException.ThrowIfNull(analystAgentFactory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 在构造函数中创建 Agent（确保 tools 配置正确）
        _coordinatorAgent = analystAgentFactory.CreateAnalyst(AnalystType.CoordinatorAnalyst);

        _logger.LogInformation("协调分析师 Agent 已创建（支持工具调用 + 结构化输出）");
    }

    /// <summary>
    /// 处理聚合的分析师消息，生成并返回最终分析报告
    /// </summary>
    public override async ValueTask<MarketAnalysisReport> HandleAsync(
        List<ChatMessage> analystMessages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("🔍 [DEBUG] CoordinatorExecutor.HandleAsync 被调用，收到 {Count} 条消息", analystMessages?.Count ?? 0);

        ArgumentNullException.ThrowIfNull(analystMessages);

        if (analystMessages.Count == 0)
        {
            throw new ArgumentException("没有分析师数据", nameof(analystMessages));
        }

        // 从工作流状态读取股票代码
        var stockSymbol = await context.ReadStateAsync<string>(WorkflowStateKeys.StockSymbol, WorkflowStateKeys.Scope, cancellationToken);

        if (string.IsNullOrWhiteSpace(stockSymbol))
        {
            throw new InvalidOperationException("无法从工作流状态中获取股票代码");
        }

        _logger.LogInformation(
            "协调分析师开始生成最终报告，股票: {StockSymbol}, 分析师数量: {Count}",
            stockSymbol,
            analystMessages.Count);

        try
        {
            // 构建聊天消息列表
            var messages = new List<ChatMessage>(analystMessages)
            {
                // 添加用户请求：生成综合报告
                new ChatMessage(
                ChatRole.User,
                $"请基于以上所有分析师的专业意见，为股票 {stockSymbol} 生成一份综合分析报告。")
            };

            // 使用带结构化输出的 ChatClientAgent 运行
            var agentResponse = await _coordinatorAgent.RunAsync(
                messages,
                thread: null,
                options: null,
                cancellationToken);

            // 提取协调分析师的回复（最后一条 Assistant 消息）
            var coordinatorMessage = agentResponse.Messages
                .LastOrDefault(m => m.Role == ChatRole.Assistant);

            if (coordinatorMessage == null)
            {
                throw new InvalidOperationException("协调分析师未能生成报告");
            }

            // 🎉 直接反序列化为 CoordinatorResult

            var coordinatorResult = agentResponse.Deserialize<CoordinatorResult>(JsonOptions);

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
                StockSymbol = stockSymbol,
                AnalystMessages = new List<ChatMessage>(analystMessages)
                {
                    coordinatorMessage
                },
                CoordinatorResult = coordinatorResult,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("协调分析师已完成最终报告生成，股票: {StockSymbol}",
                stockSymbol);

            return finalReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "协调分析师生成报告时发生错误，股票: {StockSymbol}",
                await context.ReadStateAsync<string>(WorkflowStateKeys.StockSymbol, cancellationToken) ?? "未知");
            throw;
        }
    }
}
