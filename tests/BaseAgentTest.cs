using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// Agent Framework 测试基类
/// 使用 AddApplicationServices 注册所有应用服务
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
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 从环境变量获取ApiKey
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");
        var zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? throw new InvalidOperationException("ZHITU_API_TOKEN environment variable is not set");
        var searchApiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_API_KEY") ?? throw new InvalidOperationException("WEB_SEARCH_API_KEY environment variable is not set");

        // 硬编码ModelId和Endpoint
        var modelId = "deepseek-ai/DeepSeek-V3.2";
        var endpoint = "https://api.siliconflow.cn";

        // 注册用户设置服务（Mock）
        services.AddSingleton<IUserSettingService>(provider =>
        {
            var testUserSetting = new UserSetting
            {
                ZhiTuApiToken = zhiTuApiToken,
                ModelId = modelId,
                EmbeddingModelId = "BAAI/bge-m3",
                Endpoint = endpoint,
                ApiKey = apiKey,
                EnabledAnalystRoles = new Dictionary<string, bool>
                {
                    { "FinancialAnalystAgent", true },
                    { "MarketSentimentAnalystAgent", false },
                    { "TechnicalAnalystAgent", false },
                    { "NewsEventAnalystAgent", true }
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

        // 使用 AddApplicationServices 注册所有应用服务
        services.AddApplicationServices();

        return services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task BaseCleanupAsync()
    {
        switch (_serviceProvider)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}