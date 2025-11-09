using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 协调分析师 Executor（优化版：使用框架原生结构化输出）
/// 负责汇总各分析师的分析并生成最终报告
/// 使用 ChatClientAgent 支持工具调用 + 结构化输出
/// </summary>
public sealed class CoordinatorExecutor : Executor<AggregatedAnalysisResult, MarketAnalysisReport>
{
    private readonly ChatClientAgent _coordinatorAgent;
    private readonly ILogger<CoordinatorExecutor> _logger;

    public CoordinatorExecutor(
        IAnalystAgentFactory analystAgentFactory,
        ILogger<CoordinatorExecutor> logger)
        : base("Coordinator")
    {
        ArgumentNullException.ThrowIfNull(analystAgentFactory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 在构造函数中创建 Agent（确保 tools 配置正确）
        _coordinatorAgent = analystAgentFactory.CreateAnalyst(AnalysisAgent.CoordinatorAnalyst);

        _logger.LogInformation("协调分析师 Agent 已创建（支持工具调用 + 结构化输出）");
    }

    /// <summary>
    /// 处理聚合结果，生成并返回最终分析报告
    /// </summary>
    public override async ValueTask<MarketAnalysisReport> HandleAsync(
        AggregatedAnalysisResult aggregatedResult,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregatedResult);
        ArgumentNullException.ThrowIfNull(aggregatedResult.OriginalRequest);

        if (aggregatedResult.AnalystMessages == null || aggregatedResult.AnalystMessages.Count == 0)
        {
            throw new ArgumentException("聚合结果中没有分析师数据", nameof(aggregatedResult));
        }

        try
        {
            _logger.LogInformation(
                "协调分析师开始生成最终报告，股票: {StockSymbol}, 分析师数量: {Count}",
                aggregatedResult.OriginalRequest.StockSymbol,
                aggregatedResult.AnalystMessages.Count);

            // 构建聊天消息列表
            var messages = new List<ChatMessage>(aggregatedResult.AnalystMessages);

            // 添加用户请求：生成综合报告
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"请基于以上所有分析师的专业意见，为股票 {aggregatedResult.OriginalRequest.StockSymbol} 生成一份综合分析报告。"));

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

            _logger.LogInformation(
                "协调分析师生成报告完成，调用了 {ToolCount} 次工具",
                agentResponse.Messages.Count(m => m.Contents.Any(c => c is FunctionCallContent)));

            // 🎉 直接反序列化为 CoordinatorResult
            var coordinatorResult = agentResponse.Deserialize<CoordinatorResult>(JsonSerializerOptions.Web);

            if (coordinatorResult == null)
            {
                throw new InvalidOperationException("协调分析师未能返回结构化数据");
            }

            _logger.LogInformation(
                "成功获取协调分析师的结构化数据，综合评分: {Score}，最终评级: {Rating}",
                coordinatorResult.OverallScore,
                coordinatorResult.InvestmentRating);

            // 构建完整的对话历史（包括各分析师的分析结果和协调分析师的总结）
            var fullChatHistory = new List<ChatMessage>(aggregatedResult.AnalystMessages);
            fullChatHistory.Add(coordinatorMessage);

            // 创建最终报告
            var finalReport = new MarketAnalysisReport
            {
                StockSymbol = aggregatedResult.OriginalRequest.StockSymbol,
                AnalystMessages = aggregatedResult.AnalystMessages,
                CoordinatorResult = coordinatorResult,
                ChatHistory = fullChatHistory,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("协调分析师已完成最终报告生成，股票: {StockSymbol}",
                aggregatedResult.OriginalRequest.StockSymbol);

            // 返回最终报告（框架会自动传递）
            return finalReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "协调分析师生成报告时发生错误，股票: {StockSymbol}",
                aggregatedResult.OriginalRequest.StockSymbol);
            throw;
        }
    }
}
