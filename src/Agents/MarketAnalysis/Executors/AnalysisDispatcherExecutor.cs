using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析分发器 Executor（基于官方 Executor<T> 模式）
/// 负责将用户请求转换为 ChatMessage 并实现 Fan-Out 模式
/// 使用工作流状态管理传递配置给 Aggregator
/// </summary>
public sealed class AnalysisDispatcherExecutor : Executor<MarketAnalysisRequest>
{
    private const string AnalysisPromptTemplate = "请对股票 {0} 进行专业分析，提供投资建议。";
    private const string StateKeyStockSymbol = "stockSymbol";
    private const string StateKeyExpectedCount = "expectedAnalystCount";

    private readonly ILogger<AnalysisDispatcherExecutor> _logger;

    public AnalysisDispatcherExecutor(ILogger<AnalysisDispatcherExecutor> logger)
        : base("AnalysisDispatcher")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理分析请求，通过状态管理传递配置，通过消息发送分析任务
    /// </summary>
    public override async ValueTask HandleAsync(
        MarketAnalysisRequest request,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.StockSymbol))
        {
            throw new ArgumentException("股票代码不能为空", nameof(request));
        }

        if (request.ExpectedAnalystCount <= 0)
        {
            throw new ArgumentException("预期分析师数量必须大于0", nameof(request));
        }

        try
        {
            _logger.LogInformation("分发器开始处理股票 {StockSymbol} 的分析请求", request.StockSymbol);

            // 使用状态管理传递配置数据给 Aggregator（替代消息传递）
            await context.QueueStateUpdateAsync(StateKeyStockSymbol, request.StockSymbol, cancellationToken);
            await context.QueueStateUpdateAsync(StateKeyExpectedCount, request.ExpectedAnalystCount, cancellationToken);

            _logger.LogDebug("已将配置写入工作流状态: StockSymbol={StockSymbol}, ExpectedCount={Count}",
                request.StockSymbol, request.ExpectedAnalystCount);

            // 构建分析提示词并发送给所有分析师（Fan-Out）
            string prompt = string.Format(AnalysisPromptTemplate, request.StockSymbol);
            await context.SendMessageAsync(new ChatMessage(ChatRole.User, prompt), cancellationToken);

            _logger.LogInformation(
                "分发器已将分析任务分发给所有分析师，股票: {StockSymbol}, 预期分析师数: {Count}",
                request.StockSymbol, request.ExpectedAnalystCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分发分析请求时发生错误，股票代码: {StockSymbol}", request.StockSymbol);
            throw;
        }
    }
}
