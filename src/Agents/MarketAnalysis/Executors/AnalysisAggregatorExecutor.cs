using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析聚合器 Executor（符合框架设计：纯聚合逻辑）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/user-guide/workflows/orchestrations/concurrent
/// 
/// 职责：接收框架自动收集的所有分析师消息，转换为 AggregatedAnalysisResult
/// 注意：框架的 BuildConcurrent 会自动并发执行和收集结果，聚合器只需处理收集好的数据
/// </summary>
public sealed class AnalysisAggregatorExecutor : Executor<IEnumerable<ChatMessage>, AggregatedAnalysisResult>
{
    private const string StateKeyStockSymbol = "stockSymbol";
    private const string StateKeyExpectedCount = "expectedAnalystCount";

    private readonly ILogger<AnalysisAggregatorExecutor> _logger;

    public AnalysisAggregatorExecutor(
        ILogger<AnalysisAggregatorExecutor> logger)
        : base(id: "AnalysisAggregator")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 聚合框架收集好的所有分析师消息
    /// 框架会自动并发执行所有分析师，并将结果作为 IEnumerable<ChatMessage> 传递过来
    /// </summary>
    public override async ValueTask<AggregatedAnalysisResult> HandleAsync(
        IEnumerable<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        try
        {
            // 从工作流状态读取请求信息
            var stockSymbol = await context.ReadStateAsync<string>(StateKeyStockSymbol, cancellationToken);
            var expectedCount = await context.ReadStateAsync<int>(StateKeyExpectedCount, cancellationToken);

            // 直接使用 ChatMessage，无需额外转换
            var analystMessages = messages
                .Where(m => m.Role == ChatRole.Assistant)  // 只处理 Assistant 消息
                .ToList();

            _logger.LogInformation(
                "框架自动收集完成，共 {Count} 个分析师的结果，准备传递给 Coordinator",
                analystMessages.Count);

            // 返回聚合结果
            return new AggregatedAnalysisResult
            {
                AnalystMessages = analystMessages,
                OriginalRequest = new MarketAnalysisRequest
                {
                    StockSymbol = stockSymbol ?? string.Empty,
                    ExpectedAnalystCount = expectedCount
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "聚合分析结果时发生错误");
            throw;
        }
    }
}
