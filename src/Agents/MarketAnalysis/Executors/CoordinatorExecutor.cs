using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 协调分析师 Executor（基于 Agent Framework 最佳实践）
/// 负责汇总各分析师的分析并生成最终报告
/// </summary>
internal sealed class CoordinatorExecutor :
    ReflectingExecutor<CoordinatorExecutor>,
    IMessageHandler<AggregatedAnalysisResult>
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<CoordinatorExecutor> _logger;

    public CoordinatorExecutor(
        IChatClient chatClient,
        ILogger<CoordinatorExecutor> logger)
        : base(id: "Coordinator")
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理聚合结果，生成最终分析报告
    /// </summary>
    public async ValueTask HandleAsync(
        AggregatedAnalysisResult aggregatedResult,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("协调分析师开始生成最终报告");

            // 加载协调分析师的 YAML 配置
            string yamlPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Agents",
                "Yaml",
                $"{AnalysisAgents.CoordinatorAnalystAgent}.yaml");

            string systemPrompt = "你是一位专业的投资协调分析师，负责整合多位分析师的意见，生成一份全面、客观、有深度的综合分析报告。";
            
            if (File.Exists(yamlPath))
            {
                string yamlContent = await File.ReadAllTextAsync(yamlPath, cancellationToken);
                PromptTemplateConfig templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);
                
                if (!string.IsNullOrWhiteSpace(templateConfig.Template))
                {
                    systemPrompt = templateConfig.Template;
                }
            }
            else
            {
                _logger.LogWarning("未找到协调分析师配置文件，使用默认提示词");
            }

            // 构建聊天消息列表（使用 Microsoft.Extensions.AI.ChatMessage）
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, systemPrompt)
            };

            // 添加各分析师的分析结果
            foreach (var result in aggregatedResult.AnalystResults)
            {
                messages.Add(new ChatMessage(
                    ChatRole.Assistant, 
                    $"【{result.AnalystName}的分析】\n{result.Content}")
                {
                    AuthorName = result.AnalystName
                });
            }

            // 添加用户请求：生成综合报告
            messages.Add(new ChatMessage(
                ChatRole.User, 
                $"请基于以上所有分析师的专业意见，为股票 {aggregatedResult.OriginalRequest.StockSymbol} 生成一份综合分析报告。"));

            // 调用 ChatClient 生成总结
            var chatCompletion = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            string coordinatorSummary = chatCompletion?.Text ?? "协调分析师未能生成报告";

            // 构建完整的对话历史（使用 Semantic Kernel ChatHistory 以兼容现有系统）
            var fullChatHistory = new ChatHistory();
            
            foreach (var result in aggregatedResult.AnalystResults)
            {
                fullChatHistory.Add(new ChatMessageContent(
                    AuthorRole.Assistant,
                    result.Content)
                {
                    AuthorName = result.AnalystName
                });
            }
            
            fullChatHistory.Add(new ChatMessageContent(
                AuthorRole.Assistant,
                coordinatorSummary)
            {
                AuthorName = "CoordinatorAnalystAgent"
            });

            // 创建最终报告
            var finalReport = new MarketAnalysisReport
            {
                StockSymbol = aggregatedResult.OriginalRequest.StockSymbol,
                AnalystResults = aggregatedResult.AnalystResults,
                CoordinatorSummary = coordinatorSummary,
                ChatHistory = fullChatHistory,
                CreatedAt = DateTime.UtcNow
            };

            // 输出最终报告
            await context.YieldOutputAsync(finalReport, cancellationToken);

            _logger.LogInformation("协调分析师已完成最终报告生成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "协调分析师生成报告时发生错误");
            throw;
        }
    }
}
