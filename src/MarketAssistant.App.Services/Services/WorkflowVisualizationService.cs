using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services;

/// <summary>
/// 工作流可视化服务，将 MAF Workflow 导出为 Mermaid 图表
/// </summary>
public class WorkflowVisualizationService
{
    private readonly ILogger<WorkflowVisualizationService> _logger;

    public WorkflowVisualizationService(ILogger<WorkflowVisualizationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 将工作流导出为 Mermaid 格式图表
    /// </summary>
    public string ExportToMermaid(Workflow? workflow = null)
    {
        // MAF 1.0.0 暂未公开 ToMermaid()，使用预定义模板
        return GenerateFallbackMermaid();
    }

    /// <summary>
    /// 静态回退 Mermaid 图（当 MAF API 不可用时）
    /// </summary>
    private static string GenerateFallbackMermaid()
    {
        return """
            graph TD
                A[Dispatcher<br/>分发分析任务] --> B[技术分析师]
                A --> C[基本面分析师]
                A --> D[情绪分析师]
                A --> E[新闻分析师]
                A --> F[财务分析师]
                B --> G[Aggregator<br/>聚合分析结果]
                C --> G
                D --> G
                E --> G
                F --> G
                G --> H[Coordinator<br/>生成综合报告]
                H --> I[MarketAnalysisReport]
            """;
    }
}
