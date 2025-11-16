using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析分发器 Executor（基于官方 Fan-Out 模式）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-concurrent-workflow
/// 
/// 职责：
/// 1. 接收股票代码
/// 2. 保存必要的配置到 workflow state
/// 3. 广播消息给所有分析师（通过 SendMessageAsync）
/// 4. 发送 TurnToken 触发分析师开始处理
/// </summary>
public sealed class AnalysisDispatcherExecutor : Executor<string>
{
    private const string AnalysisPromptTemplate = "请对股票 {0} 进行专业分析，提供投资建议。";

    private readonly int _expectedAnalystCount;
    private readonly ILogger<AnalysisDispatcherExecutor> _logger;

    public AnalysisDispatcherExecutor(
        int expectedAnalystCount,
        ILogger<AnalysisDispatcherExecutor> logger)
        : base("AnalysisDispatcher")
    {
        _expectedAnalystCount = expectedAnalystCount;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理股票代码，广播分析任务给所有分析师
    /// </summary>
    public override async ValueTask HandleAsync(
        string stockSymbol,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stockSymbol))
        {
            throw new ArgumentException("股票代码不能为空", nameof(stockSymbol));
        }

        try
        {
            _logger.LogInformation(
                "分发器开始处理股票 {StockSymbol} 的分析请求，期望 {Count} 位分析师",
                stockSymbol, _expectedAnalystCount);

            // 保存配置到 workflow state
            await context.QueueStateUpdateAsync(WorkflowStateKeys.StockSymbol, stockSymbol, cancellationToken);
            await context.QueueStateUpdateAsync(WorkflowStateKeys.ExpectedAnalystCount, _expectedAnalystCount, cancellationToken);

            // 构建分析提示词并广播给所有分析师（Fan-Out）
            // 注意：接收的 Agent 会排队消息，但不会立即处理，直到收到 TurnToken
            string prompt = string.Format(AnalysisPromptTemplate, stockSymbol);
            await context.SendMessageAsync(new ChatMessage(ChatRole.User, prompt), cancellationToken);

            // 发送 TurnToken 触发所有分析师开始处理
            await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken);

            _logger.LogInformation(
                "分发器已将分析任务分发给 {Count} 位分析师，股票: {StockSymbol}",
                _expectedAnalystCount, stockSymbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分发分析请求时发生错误，股票代码: {StockSymbol}", stockSymbol);
            throw;
        }
    }
}
