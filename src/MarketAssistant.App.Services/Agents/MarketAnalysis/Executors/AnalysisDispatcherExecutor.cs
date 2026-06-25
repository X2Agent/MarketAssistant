using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析分发器 Executor（基于官方 Fan-Out 模式）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-concurrent-workflow
///
/// 职责：
/// 1. 接收标的代码
/// 2. 保存必要的配置到 workflow state
/// 3. 广播 ChatMessage 给所有分析师（通过 SendMessageAsync）
/// 4. 广播 TurnToken 触发分析师开始处理（AIAgent 收到 ChatMessage 后不会自动处理，必须收到 TurnToken 才会调用 LLM）
/// </summary>
[SendsMessage(typeof(ChatMessage))]
[SendsMessage(typeof(TurnToken))]
public sealed partial class AnalysisDispatcherExecutor : Executor
{
    private const string AnalysisPromptTemplate = "请对标的 {0} 进行专业分析，提供投资建议。";

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

    [MessageHandler]
    private async ValueTask HandleAsync(
        string assetSymbol,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetSymbol))
        {
            throw new ArgumentException("标的代码不能为空", nameof(assetSymbol));
        }

        try
        {
            _logger.LogInformation(
                "分发器开始处理标的 {AssetSymbol} 的分析请求，期望 {Count} 位分析师",
                assetSymbol, _expectedAnalystCount);

            // https://github.com/microsoft/agent-framework/issues/2162
            // 保存配置到 workflow state（显式指定 scope 确保跨 Executor 可见）
            await context.QueueStateUpdateAsync(
                WorkflowStateKeys.AssetSymbol,
                assetSymbol,
                WorkflowStateKeys.Scope,
                cancellationToken);
            await context.QueueStateUpdateAsync(
                WorkflowStateKeys.ExpectedAnalystCount,
                _expectedAnalystCount,
                WorkflowStateKeys.Scope,
                cancellationToken);

            // 1. 广播 ChatMessage 给所有分析师（AIAgent 会缓存消息但不会开始处理）
            string prompt = string.Format(AnalysisPromptTemplate, assetSymbol);
            await context.SendMessageAsync(
                new ChatMessage(ChatRole.User, prompt),
                cancellationToken);

            // 2. 广播 TurnToken 触发分析师开始处理（关键步骤！）
            // AIAgent 是"懒加载"的，只有收到 TurnToken 才会调用 LLM 处理缓存的消息
            await context.SendMessageAsync(
                new TurnToken(emitEvents: true),
                cancellationToken);

            _logger.LogInformation(
                "分发器已将分析任务分发给 {Count} 位分析师，标的: {AssetSymbol}",
                _expectedAnalystCount, assetSymbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分发分析请求时发生错误，标的代码: {AssetSymbol}", assetSymbol);
            throw;
        }
    }
}
