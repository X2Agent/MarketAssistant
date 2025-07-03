using MarketAssistant.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration.GroupChat;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

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
    private GroupChatOrchestration _chat;

    private Action<ChatMessageContent>? _messageCallback;

    public ChatHistory History { get; } = [];

    public AnalystManager(Kernel kernel, ILogger<AnalystManager> logger, IUserSettingService userSettingService)
    {
        _kernel = kernel;
        _logger = logger;
        _userSettingService = userSettingService;
        // 创建分析师团队
        CreateAnalystTeam();
        // 初始化Agent聊天组
        InitializeAgentGroupChat();
    }

    /// <summary>
    /// 创建分析师团队
    /// </summary>
    private void CreateAnalystTeam()
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

        // 协调分析师始终启用，因为它负责引导讨论和总结
        AddAnalystIfEnabled(roleSettings.EnableAnalysisSynthesizer, AnalysisAgents.CoordinatorAnalystAgent);
    }

    /// <summary>
    /// 根据条件添加分析师
    /// </summary>
    private void AddAnalystIfEnabled(bool isEnabled, AnalysisAgents agent)
    {
        if (isEnabled)
        {
            _analysts.Add(CreateAnalyst(agent));
        }
    }

    /// <summary>
    /// 创建分析师
    /// </summary>
    private ChatCompletionAgent CreateAnalyst(AnalysisAgents agent)
    {
        string agentYamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Agents", "yaml", $"{agent}.yaml");
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

        var promptExecutionSettings = new OpenAIPromptExecutionSettings()
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new()
            {
                AllowParallelCalls = false,
                AllowStrictSchemaAdherence = false,
                RetainArgumentTypes = true
            }),
            //TopP = 1
        };

        ChatCompletionAgent chatCompletionAgent =
            new ChatCompletionAgent(templateConfig, new KernelPromptTemplateFactory())
            {
                Kernel = _kernel,
                // Provide default values for template parameters
                Arguments = new KernelArguments(promptExecutionSettings)
                {
                    { "global_analysis_guidelines", globalGuidelines },
                }
            };
        return chatCompletionAgent;
    }

    ValueTask responseCallback(ChatMessageContent response)
    {
        History.Add(response);
        _messageCallback?.Invoke(response);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 初始化Agent聊天组
    /// </summary>
    private void InitializeAgentGroupChat()
    {
        var groupChatManager = new AnalysisGroupChatManager()
        {
            MaximumInvocationCount = 10
        };

        _chat = new GroupChatOrchestration(groupChatManager, _analysts.ToArray())
        {
            ResponseCallback = responseCallback
        };
    }

    public async Task ExecuteAnalystDiscussionAsync(string prompt, Action<ChatMessageContent>? messageCallback)
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
        var result = await _chat.InvokeAsync(prompt, _runtime);
        var response = await result.GetValueAsync(TimeSpan.FromMinutes(10));

        // 等待运行时完成
        await _runtime.RunUntilIdleAsync();
        await _runtime.StopAsync();
        _logger.LogInformation("分析已停止。");
    }


    /// <summary>
    /// 获取分析师列表
    /// </summary>
    /// <returns>分析师列表</returns>
    public List<ChatCompletionAgent> GetAnalysts()
    {
        return _analysts;
    }

    // 配置上下文函数提供者
    //var whiteboardProvider = new WhiteboardProvider(chatClient);
    //var agentThread = new ChatHistoryAgentThread();
    //agentThread.AIContextProviders.Add(whiteboardProvider);
    //agentThread.AIContextProviders.Add(
    //        new ContextualFunctionProvider(
    //            vectorStore: new InMemoryVectorStore(new InMemoryVectorStoreOptions()
    //{
    //    EmbeddingGenerator = embeddingGenerator
    //            }),
    //            vectorDimensions: 1536,
    //            functions: GetMarketSentimentRelevantFunctions(), // 只包含情绪分析相关函数
    //            maxNumberOfFunctions: 3, // 限制最多3个相关函数
    //            loggerFactory: LoggerFactory
    //        )
    //    );

    private static IReadOnlyList<AIFunction> GetMarketSentimentRelevantFunctions()
    {
        return
        [
            // 只包含与市场情绪分析相关的函数
            AIFunctionFactory.Create(() => "获取资金流向数据", "GetCapitalFlow"),
            AIFunctionFactory.Create(() => "获取投资者情绪指数", "GetSentimentIndex"),
            AIFunctionFactory.Create(() => "获取机构持仓变化", "GetInstitutionalHoldings"),
        ];
    }

    private static IReadOnlyList<AIFunction> GetTechnicalAnalysisRelevantFunctions()
    {
        return
        [
            AIFunctionFactory.Create(() => "获取KDJ指标", "GetStockKDJ"),
            AIFunctionFactory.Create(() => "获取MACD指标", "GetStockMACD"),
            AIFunctionFactory.Create(() => "获取BOLL指标", "GetStockBOLL"),
            AIFunctionFactory.Create(() => "获取MA指标", "GetStockMA"),
        ];
    }

    private static IReadOnlyList<AIFunction> GetFundamentalAnalysisRelevantFunctions()
    {
        return
        [
            AIFunctionFactory.Create(() => "获取财务数据", "GetFinancialData"),
            AIFunctionFactory.Create(() => "获取公司基本信息", "GetCompanyInfo"),
            // 其他基本面相关函数
        ];
    }
}