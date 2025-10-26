using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析分发器 Executor（基于 Agent Framework 最佳实践）
/// 负责将用户请求广播给所有分析师代理，实现 Fan-Out 模式
/// </summary>
internal sealed class AnalysisDispatcherExecutor :
    ReflectingExecutor<AnalysisDispatcherExecutor>,
    IMessageHandler<MarketAnalysisRequest>
{
    private readonly ILogger<AnalysisDispatcherExecutor> _logger;

    public AnalysisDispatcherExecutor(ILogger<AnalysisDispatcherExecutor> logger)
        : base(id: "AnalysisDispatcher")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理分析请求，构建提示词并广播给所有分析师
    /// </summary>
    public async ValueTask HandleAsync(
        MarketAnalysisRequest request,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("分发器开始处理股票 {StockSymbol} 的分析请求", request.StockSymbol);

            // 构建分析提示词
            string prompt = string.IsNullOrWhiteSpace(request.Prompt)
                ? $"请对股票 {request.StockSymbol} 进行专业分析，提供投资建议。"
                : request.Prompt;

            // 创建用户消息，广播给所有下游分析师
            var userMessage = new ChatMessage(ChatRole.User, prompt);

            // 发送消息到下游节点（所有分析师）
            await context.SendMessageAsync(userMessage, cancellationToken);

            _logger.LogInformation("分发器已将分析请求广播给所有分析师");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分发分析请求时发生错误");
            throw;
        }
    }
}

