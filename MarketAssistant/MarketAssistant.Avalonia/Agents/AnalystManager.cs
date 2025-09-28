using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration.Concurrent;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Agents;

/// <summary>
/// 分析师管理器，负责创建分析师团队和管理分析师之间的对话
/// </summary>
public class AnalystManager
{
    private readonly Kernel _kernel;
    private readonly ILogger<AnalystManager> _logger;
    private readonly IUserSettingService _userSettingService;
    private readonly List<ChatCompletionAgent> _analysts = new();
    private ConcurrentOrchestration _orchestration;
    private readonly IKernelPluginConfig _kernelPluginConfig;

    private Action<ChatMessageContent>? _messageCallback;

    /// <summary>
    /// 添加到历史记录每个AuthorName可能会有多条
    /// eg.
    /// 一个AuthorName三条记录
    /// 对应的Role分别为Assistant、Tool、Assistant
    /// 分别对应是否调用Plugin的分析(Content="\n\n")、工具调用(Content="Plugin返回数据")和分析师的回复(Content="最终结果")
    /// </summary>
    public ChatHistory History { get; } = [];

    public AnalystManager(Kernel kernel,
        ILogger<AnalystManager> logger,
        IUserSettingService userSettingService,
        IKernelPluginConfig kernelPluginConfig)
    {
        _kernel = kernel;
        _logger = logger;
        _userSettingService = userSettingService;
        _kernelPluginConfig = kernelPluginConfig;

        // 创建分析师及编排
        CreateAnalysts();
    }

    /// <summary>
    /// 创建分析师团队
    /// </summary>
    private void CreateAnalysts()
    {
        // 获取用户设置
        var roleSettings = _userSettingService.CurrentSetting.AnalystRoleSettings;

        // 基本面分析师 - 整合了策略分析师和股票研究分析师的功能
        AddAnalystIfEnabled(roleSettings.EnableFundamentalAnalyst, AnalysisAgents.FundamentalAnalystAgent);

        // 市场情绪分析师 - 整合了行为金融分析师和市场分析师的功能
        AddAnalystIfEnabled(roleSettings.EnableMarketSentimentAnalyst, AnalysisAgents.MarketSentimentAnalystAgent);

        // 财务分析师
        AddAnalystIfEnabled(roleSettings.EnableFinancialAnalyst, AnalysisAgents.FinancialAnalystAgent);

        // 技术分析师
        AddAnalystIfEnabled(roleSettings.EnableTechnicalAnalyst, AnalysisAgents.TechnicalAnalystAgent);

        // 新闻事件分析师
        AddAnalystIfEnabled(roleSettings.EnableNewsEventAnalyst, AnalysisAgents.NewsEventAnalystAgent);

        /// <summary>
        /// 根据条件添加分析师
        /// </summary>
        void AddAnalystIfEnabled(bool isEnabled, AnalysisAgents agent)
        {
            if (isEnabled)
            {
                _analysts.Add(CreateAnalyst(agent));
            }
        }

        _orchestration = new ConcurrentOrchestration(_analysts.ToArray())
        {
            ResponseCallback = responseCallback
        };
    }

    /// <summary>
    /// 创建分析师
    /// </summary>
    private ChatCompletionAgent CreateAnalyst(AnalysisAgents agent)
    {
        string agentYamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Agents", "Yaml", $"{agent}.yaml");
        if (!File.Exists(agentYamlPath))
        {
            _logger.Equals($"未找到分析师配置文件: {agentYamlPath}。请确保已正确配置并放置在Agents/yaml目录下。");
            throw new Exception($"未找到分析师配置文件: {agentYamlPath}。请确保已正确配置并放置在Agents/yaml目录下。");
        }

        string yamlContent = File.ReadAllText(agentYamlPath);
        PromptTemplateConfig templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);
        _logger.LogInformation("成功加载备用YAML配置: {AgentName}", agent);

        var globalGuidelines = @"
        ## 分析准则
        - 采用1-10分量化评估
        - 提供具体价格点位和数值区间
        - 控制总字数300字内
        - 直接输出专业分析，无需询问
        ";

        var kernel = _kernelPluginConfig.PluginConfig(_kernel, agent);

        ChatCompletionAgent chatCompletionAgent =
            new ChatCompletionAgent(templateConfig, new KernelPromptTemplateFactory())
            {
                Kernel = kernel
            };
        chatCompletionAgent.Arguments!["global_analysis_guidelines"] = globalGuidelines;

        return chatCompletionAgent;
    }

    ValueTask responseCallback(ChatMessageContent response)
    {
        History.Add(response);
        _messageCallback?.Invoke(response);
        return ValueTask.CompletedTask;
    }

    public async Task<string[]> ExecuteAnalystDiscussionAsync(string prompt, Action<ChatMessageContent>? messageCallback)
    {
        _messageCallback = messageCallback;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("提示词不能为空。", nameof(prompt));
        }

        // 初始化运行时
        var _runtime = new InProcessRuntime();
        await _runtime.StartAsync();

        // 执行分析
        var result = await _orchestration.InvokeAsync(prompt, _runtime);
        var response = await result.GetValueAsync(TimeSpan.FromMinutes(10));

        // 等待运行时完成
        await _runtime.RunUntilIdleAsync();
        await _runtime.StopAsync();
        _logger.LogInformation("分析已停止。");

        return response;
    }

    /// <summary>
    /// 执行协调分析师分析
    /// </summary>
    public async Task<ChatMessageContent> ExecuteCoordinatorAnalysisAsync(string[] analystResults)
    {
        var coordinatorAgent = CreateAnalyst(AnalysisAgents.CoordinatorAnalystAgent);
        coordinatorAgent.Arguments!["history"] = analystResults.ToList();

        var agentResponses = await coordinatorAgent.InvokeAsync().ToListAsync();

        return agentResponses.LastOrDefault() ?? throw new InvalidOperationException("协调分析师未返回任何响应");
    }
}