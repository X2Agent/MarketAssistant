using MarketAssistant.Agents.MarketAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Agents;

/// <summary>
/// 市场分析代理（使用 Agent Framework Workflows 并发工作流）
/// </summary>
public class MarketAnalysisAgent
{
    #region 事件定义

    /// <summary>
    /// 分析进度变化事件
    /// </summary>
    public event EventHandler<AnalysisProgressEventArgs> ProgressChanged;

    /// <summary>
    /// 分析完成事件
    /// </summary>
    public event EventHandler<ChatMessageContent> AnalysisCompleted;

    #endregion

    #region 私有字段

    private readonly MarketAnalysisWorkflow _analysisWorkflow;
    private readonly ILogger<MarketAnalysisAgent> _logger;

    #endregion

    #region 构造函数

    public MarketAnalysisAgent(
        ILogger<MarketAnalysisAgent> logger,
        MarketAnalysisWorkflow analysisWorkflow)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analysisWorkflow = analysisWorkflow ?? throw new ArgumentNullException(nameof(analysisWorkflow));

        // 订阅工作流的进度事件
        _analysisWorkflow.ProgressChanged += OnWorkflowProgressChanged;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 执行股票分析
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <returns>分析消息列表</returns>
    public async Task<ChatHistory> AnalysisAsync(string stockSymbol)
    {
        try
        {
            _logger.LogInformation("开始分析股票: {StockSymbol}", stockSymbol);

            // 执行并发工作流分析
            var report = await _analysisWorkflow.AnalyzeAsync(stockSymbol);

            // 触发分析完成事件
            var coordinatorMessage = new ChatMessageContent(
                AuthorRole.Assistant,
                report.CoordinatorSummary)
            {
                AuthorName = "CoordinatorAnalystAgent"
            };
            AnalysisCompleted?.Invoke(this, coordinatorMessage);

            _logger.LogInformation("股票分析完成: {StockSymbol}", stockSymbol);

            return report.ChatHistory;
        }
        catch (Exception ex)
        {
            // 记录详细错误信息
            string errorMessage = $"分析股票 {stockSymbol} 时发生错误: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $"\n内部错误: {ex.InnerException.Message}";
            }

            _logger.LogError(ex, errorMessage);

            // 更新进度为错误状态
            OnProgressChanged(new AnalysisProgressEventArgs
            {
                CurrentAnalyst = "系统",
                StageDescription = $"分析失败: {ex.Message}",
                IsInProgress = false
            });

            throw;
        }
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 工作流进度变化事件处理
    /// </summary>
    private void OnWorkflowProgressChanged(object? sender, AnalysisProgressEventArgs e)
    {
        // 转发工作流的进度事件
        ProgressChanged?.Invoke(this, e);
    }

    /// <summary>
    /// 触发进度变化事件
    /// </summary>
    protected virtual void OnProgressChanged(AnalysisProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
    }

    #endregion
}