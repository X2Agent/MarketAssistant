using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using MarketAssistant.Vectors;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration.Concurrent;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Brave;
using Microsoft.SemanticKernel.Plugins.Web.Tavily;

namespace MarketAssistant.Agents;

/// <summary>
/// 分析师管理器，负责创建分析师团队和管理分析师之间的对话
/// </summary>
public class AnalystManager
{
    #region 常量定义
    private const string WEB_SEARCH_PLUGIN_NAME = "WebSearchPlugin";
    private const string VECTOR_SEARCH_PLUGIN_NAME = "VectorSearchPlugin";
    #endregion

    private readonly Kernel _kernel;
    private readonly ILogger<AnalystManager> _logger;
    private readonly IUserSettingService _userSettingService;
    private readonly List<ChatCompletionAgent> _analysts = new();
    private ConcurrentOrchestration _orchestration;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly VectorStore _vectorStore;
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
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorStore vectorStore,
        IKernelPluginConfig kernelPluginConfig)
    {
        _kernel = kernel;
        _logger = logger;
        _userSettingService = userSettingService;
        _embeddingGenerator = embeddingGenerator;
        _vectorStore = vectorStore;
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

    #region 私有辅助方法

    /// <summary>
    /// 配置Web搜索插件
    /// </summary>
    private void ConfigureWebSearchPlugin(ChatCompletionAgent agent, UserSetting userSetting)
    {
        if (!userSetting.EnableWebSearch || string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey))
        {
            return;
        }

        try
        {
            ITextSearch textSearch = userSetting.WebSearchProvider.ToLower() switch
            {
                "bing" => new BingTextSearch(apiKey: userSetting.WebSearchApiKey),
                "brave" => new BraveTextSearch(apiKey: userSetting.WebSearchApiKey),
                "tavily" => new TavilyTextSearch(apiKey: userSetting.WebSearchApiKey),
                _ => null
            };

            if (textSearch != null)
            {
                var searchPlugin = textSearch.CreateWithGetTextSearchResults(WEB_SEARCH_PLUGIN_NAME);
                agent.Kernel.Plugins.Add(searchPlugin);
                _logger.LogInformation("成功添加 {Provider} Web搜索插件到协调分析师", userSetting.WebSearchProvider);
            }
            else
            {
                _logger.LogWarning("不支持的Web搜索提供商: {Provider}", userSetting.WebSearchProvider);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "配置Web搜索插件失败，提供商: {Provider}", userSetting.WebSearchProvider);
        }
    }

    /// <summary>
    /// 配置向量知识库搜索插件
    /// </summary>
    private void ConfigureVectorSearchPlugin(ChatCompletionAgent agent)
    {
        if (!_userSettingService.CurrentSetting.LoadKnowledge)
        {
            return;
        }

        try
        {
            var collection = _vectorStore.GetCollection<string, TextParagraph>(UserSetting.VectorCollectionName);

            // 确保集合已创建（同步等待避免更改方法签名）
            collection.EnsureCollectionExistsAsync().GetAwaiter().GetResult();

            // 创建向量存储文本搜索实例，用于从内部知识库检索内容
            var textSearch = new VectorStoreTextSearch<TextParagraph>(collection, _embeddingGenerator);

            // 自定义一个更易被模型选择的搜索函数（名称/说明/参数）
            var options = new KernelFunctionFromMethodOptions()
            {
                FunctionName = "GetTextSearchResults",
                Description = "从内部投研知识库检索与查询相关的高可信内容，返回可引用的片段。",
                Parameters =
                [
                    new KernelParameterMetadata("query") { Description = "搜索关键字或问题", IsRequired = true },
                    new KernelParameterMetadata("top") { Description = "返回条数", IsRequired = false, DefaultValue = 3 },
                    new KernelParameterMetadata("skip") { Description = "跳过条数（分页）", IsRequired = false, DefaultValue = 0 },
                ],
                ReturnParameter = new() { ParameterType = typeof(KernelSearchResults<TextSearchResult>) },
            };

            var searchPlugin = KernelPluginFactory.CreateFromFunctions(
                VECTOR_SEARCH_PLUGIN_NAME, "Search internal knowledge base for grounding",
                [textSearch.CreateGetTextSearchResults(options)]);

            agent.Kernel.Plugins.Add(searchPlugin);
            _logger.LogInformation("成功添加向量知识库搜索插件到协调分析师");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "配置向量知识库搜索插件失败");
        }
    }

    #endregion

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


    public ChatCompletionAgent CreateCoordinatorAgent()
    {
        _logger.LogInformation("开始创建协调分析师");

        var coordinatorAgent = CreateAnalyst(AnalysisAgents.CoordinatorAnalystAgent);
        var userSetting = _userSettingService.CurrentSetting;

        // 配置Web搜索插件
        ConfigureWebSearchPlugin(coordinatorAgent, userSetting);

        // 配置向量知识库搜索插件
        ConfigureVectorSearchPlugin(coordinatorAgent);

        _logger.LogInformation("协调分析师创建完成，已配置 {PluginCount} 个插件",
            coordinatorAgent.Kernel.Plugins.Count);

        return coordinatorAgent;
    }

    /// <summary>
    ///  创建一个分析师线程，使用ChatHistoryAgentThread来管理分析师之间的对话
    /// </summary>
    private ChatHistoryAgentThread CreateAgentThread()
    {
        // 这里可以配置更多的上下文提供者，例如白板、函数等
        var agentThread = new ChatHistoryAgentThread();
        //var whiteboardProvider = new WhiteboardProvider();
        //agentThread.AIContextProviders.Add(whiteboardProvider);
        //agentThread.AIContextProviders.Add(
        //        new ContextualFunctionProvider(
        //            vectorStore: new InMemoryVectorStore(new InMemoryVectorStoreOptions()
        //            {
        //                EmbeddingGenerator = embeddingGenerator
        //            }),
        //            vectorDimensions: 1536,
        //            functions: [AIFunctionFactory.Create(() => "获取资金流向数据", "GetCapitalFlow")],
        //            maxNumberOfFunctions: 3, // 限制最多3个相关函数
        //            loggerFactory: LoggerFactory
        //        )
        //    );
        return agentThread;
    }
}