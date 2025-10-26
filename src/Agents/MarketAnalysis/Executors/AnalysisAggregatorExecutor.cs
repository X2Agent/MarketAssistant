using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析聚合器 Executor（基于 Agent Framework 最佳实践）
/// 负责收集所有分析师的分析结果并聚合
/// </summary>
internal sealed class AnalysisAggregatorExecutor :
    ReflectingExecutor<AnalysisAggregatorExecutor>,
    IMessageHandler<ChatMessage>
{
    private readonly ILogger<AnalysisAggregatorExecutor> _logger;
    private readonly MarketAnalysisRequest _originalRequest;
    private readonly int _expectedAnalystCount;
    private readonly List<AnalystResult> _collectedResults = new();

    public AnalysisAggregatorExecutor(
        MarketAnalysisRequest originalRequest,
        int expectedAnalystCount,
        ILogger<AnalysisAggregatorExecutor> logger)
        : base(id: "AnalysisAggregator")
    {
        _originalRequest = originalRequest ?? throw new ArgumentNullException(nameof(originalRequest));
        _expectedAnalystCount = expectedAnalystCount;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理来自各分析师的消息并聚合结果
    /// </summary>
    public async ValueTask HandleAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 解析分析师名称和类型
            string analystName = message.AuthorName ?? "Unknown";
            AnalysisAgents analystType = ParseAnalystType(analystName);

            // 创建分析师结果（使用 Microsoft.Extensions.AI 类型）
            var analystResult = new AnalystResult
            {
                AnalystName = analystName,
                AnalystType = analystType,
                Content = message.Text ?? string.Empty,
                Role = message.Role
            };

            _collectedResults.Add(analystResult);

            _logger.LogInformation(
                "收到分析师 {AnalystName} 的结果，已收集 {Count}/{Total}",
                analystName,
                _collectedResults.Count,
                _expectedAnalystCount);

            // 当收集到所有分析师的结果时，输出聚合结果
            if (_collectedResults.Count >= _expectedAnalystCount)
            {
                var aggregatedResult = new AggregatedAnalysisResult
                {
                    AnalystResults = _collectedResults.ToList(),
                    OriginalRequest = _originalRequest
                };

                await context.SendMessageAsync(aggregatedResult, cancellationToken);

                _logger.LogInformation("所有分析师结果已聚合完成，共 {Count} 个分析师", _collectedResults.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "聚合分析结果时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 从分析师名称解析分析师类型
    /// </summary>
    private AnalysisAgents ParseAnalystType(string analystName)
    {
        return analystName switch
        {
            "FundamentalAnalystAgent" => AnalysisAgents.FundamentalAnalystAgent,
            "MarketSentimentAnalystAgent" => AnalysisAgents.MarketSentimentAnalystAgent,
            "FinancialAnalystAgent" => AnalysisAgents.FinancialAnalystAgent,
            "TechnicalAnalystAgent" => AnalysisAgents.TechnicalAnalystAgent,
            "NewsEventAnalystAgent" => AnalysisAgents.NewsEventAnalystAgent,
            _ => AnalysisAgents.FundamentalAnalystAgent
        };
    }
}
