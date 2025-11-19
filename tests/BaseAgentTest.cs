using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.StockSelection;
using MarketAssistant.Agents.StockSelection.Executors;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Extensions;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.StockScreener;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// Agent Framework 测试基类
/// </summary>
[TestClass]
public class BaseAgentTest
{
    protected ILogger? _logger;
    protected IServiceProvider _serviceProvider = null!;
    protected IChatClientFactory _chatClientFactory = null!;
    protected IAnalystAgentFactory _analystAgentFactory = null!;
    protected IHttpClientFactory _httpClientFactory = null!;
    protected IUserSettingService _userSettingService = null!;

    [TestInitialize]
    public void BaseInitialize()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<BaseAgentTest>();

        // 初始化测试所需的服务
        _serviceProvider = CreateServiceProvider();
        _chatClientFactory = _serviceProvider.GetRequiredService<IChatClientFactory>();
        _analystAgentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        _httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        _userSettingService = _serviceProvider.GetRequiredService<IUserSettingService>();
    }

    protected IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // 配置日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 从环境变量获取ApiKey
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");
        var zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? throw new InvalidOperationException("ZHITU_API_TOKEN environment variable is not set");
        var searchApiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_API_KEY") ?? throw new InvalidOperationException("WEB_SEARCH_API_KEY environment variable is not set");

        // 硬编码ModelId和Endpoint
        var modelId = "deepseek-ai/DeepSeek-V3.2-Exp";
        var endpoint = "https://api.siliconflow.cn";

        // 注册依赖服务
        services.AddSingleton(provider =>
        {
            var testUserSetting = new UserSetting
            {
                ZhiTuApiToken = zhiTuApiToken,
                ModelId = modelId,
                EmbeddingModelId = "BAAI/bge-m3",
                Endpoint = endpoint,
                ApiKey = apiKey,
                AnalystRoleSettings = new MarketAnalystRoleSettings
                {
                    EnableFinancialAnalyst = true,
                    EnableMarketSentimentAnalyst = false,
                    EnableTechnicalAnalyst = false,
                    EnableNewsEventAnalyst = true
                },
                EnableWebSearch = true,
                WebSearchApiKey = searchApiKey,
                WebSearchProvider = "Tavily",
                LoadKnowledge = true,
            };
            var userSettingServiceMock = new Mock<IUserSettingService>();
            userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(testUserSetting);
            return userSettingServiceMock.Object;
        });

        // 注册核心服务
        services.AddHttpClient();
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton<StockScreenerService>();
        services.AddSingleton<McpService>();

        // 注册 Agent Tool 类
        services.AddSingleton<StockBasicTools>();
        services.AddSingleton<StockFinancialTools>();
        services.AddSingleton<StockTechnicalTools>();
        services.AddSingleton<GroundingSearchTools>();
        services.AddSingleton<StockNewsTools>();
        services.AddSingleton<MarketSentimentTools>();

        // 注册 Agent Framework 服务
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IEmbeddingFactory, EmbeddingFactory>();
        services.AddSingleton<IAnalystAgentFactory, AnalystAgentFactory>();

        // 注册 StockSelection Workflow and Executors
        services.AddSingleton<GenerateCriteriaExecutor>();
        services.AddSingleton<ScreenStocksExecutor>();
        services.AddSingleton<AnalyzeStocksExecutor>();
        services.AddSingleton<StockSelectionWorkflow>();

        // 注册 MarketAnalysis Workflow and Executors
        services.AddSingleton<AnalysisDispatcherExecutor>();
        services.AddSingleton<AnalysisAggregatorExecutor>();
        services.AddSingleton<CoordinatorExecutor>();
        services.AddSingleton<MarketAnalysisWorkflow>();

        // 注册 RAG 服务
        services.AddRagServices();

        // 注册 Embedding Generator
        services.AddSingleton(serviceProvider =>
        {
            var embeddingFactory = serviceProvider.GetRequiredService<IEmbeddingFactory>();
            var embeddingGenerator = embeddingFactory.Create();
            return embeddingGenerator;
        });

        // 注册 Vector Store
        var store = Directory.GetCurrentDirectory() + "/vector.sqlite";
        services.AddSqliteVectorStore(_ => $"Data Source={store}");

        return services.BuildServiceProvider();
    }
}