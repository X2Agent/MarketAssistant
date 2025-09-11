using MarketAssistant.Agents;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Filtering;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using MarketAssistant.Vectors.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public class BaseKernelTest
{
    protected ILogger? _logger;
    protected Kernel _kernel = null!;
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
        _logger = loggerFactory.CreateLogger<BaseKernelTest>();

        // 初始化测试所需的服务
        _kernel = CreateKernelWithChatCompletion();
        _httpClientFactory = _kernel.Services.GetRequiredService<IHttpClientFactory>();
        _userSettingService = _kernel.Services.GetRequiredService<IUserSettingService>();
    }

    protected Kernel CreateKernelWithChatCompletion()
    {
        var builder = Kernel.CreateBuilder();

        // 配置日志
        builder.Services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Add filters with logging.
        builder.Services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
        builder.Services.AddSingleton<IPromptRenderFilter, PromptRenderLoggingFilter>();
        builder.Services.AddSingleton<IAutoFunctionInvocationFilter, AutoFunctionInvocationLoggingFilter>();

        builder.Services.AddSingleton<PlaywrightService>();
        builder.Services.AddSingleton<IKernelFactory, TestKernelFactory>();
        builder.Services.AddSingleton<StockSelectionManager>();
        builder.Services.AddHttpClient();

        // 从环境变量获取ApiKey
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");
        var zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? throw new InvalidOperationException("ZHITU_API_TOKEN environment variable is not set");
        var searchApiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_API_KEY") ?? throw new InvalidOperationException("WEB_SEARCH_API_KEY environment variable is not set");

        // 硬编码ModelId和Endpoint
        var modelId = "Qwen/Qwen3-32B";
        var endpoint = "https://api.siliconflow.cn";

        // 注册依赖服务
        builder.Services.AddSingleton(provider =>
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

        builder.Services.AddRagServices();

        builder.Services.AddKernel().AddOpenAIChatCompletion(
                modelId,
                new Uri(endpoint),
                apiKey)
            .AddOpenAITextEmbeddingGeneration(
                "BAAI/bge-m3",
                endpoint,
                apiKey)
            .Plugins
            .AddFromType<StockBasicPlugin>()
            .AddFromType<StockTechnicalPlugin>()
            .AddFromType<StockFinancialPlugin>()
            .AddFromType<StockNewsPlugin>()
            .AddFromType<StockScreenerPlugin>()
            .AddFromType<ConversationSummaryPlugin>()
            .AddFromType<GroundingSearchPlugin>()
            .AddFromType<TextPlugin>();

        var store = Directory.GetCurrentDirectory() + "/vector.sqlite";
        builder.Services.AddSqliteVectorStore(_ => $"Data Source={store}");

        builder.Services.AddSingleton<IUserEmbeddingService, UserEmbeddingService>();
        builder.Services.AddSingleton(serviceProvider =>
        {
            var userEmbeddingService = serviceProvider.GetRequiredService<IUserEmbeddingService>();
            var embeddingGenerator = userEmbeddingService.CreateEmbeddingGenerator();
            return embeddingGenerator;
        });

        return builder.Build();
    }
}

internal class TestKernelFactory : IKernelFactory
{
    private readonly Kernel _kernel;
    public TestKernelFactory(Kernel kernel) { _kernel = kernel; }
    public Kernel CreateKernel() => _kernel;
    public bool TryCreateKernel(out Kernel kernel, out string error) { kernel = _kernel; error = string.Empty; return true; }
    public void Invalidate() { }
}