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
/// 环境变量可选：缺失时使用占位值，LLM 相关测试标记为 Inconclusive
/// </summary>
public class BaseAgentTest
{
    protected ILogger? _logger;
    protected IServiceProvider _serviceProvider = null!;
    protected IChatClientFactory _chatClientFactory = null!;
    protected IAnalystAgentFactory _analystAgentFactory = null!;
    protected IHttpClientFactory _httpClientFactory = null!;
    protected IUserSettingService _userSettingService = null!;

    /// <summary>
    /// 环境中是否配置了真实 LLM API Key
    /// </summary>
    protected bool IsLlmAvailable { get; private set; }

    [TestInitialize]
    public void BaseInitialize()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<BaseAgentTest>();

        _serviceProvider = CreateServiceProvider();
        _chatClientFactory = _serviceProvider.GetRequiredService<IChatClientFactory>();
        _analystAgentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        _httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        _userSettingService = _serviceProvider.GetRequiredService<IUserSettingService>();
    }

    /// <summary>
    /// 需要真实 LLM 的测试调用此方法，缺失 API Key 时跳过而非失败
    /// </summary>
    protected void RequireLlm()
    {
        if (!IsLlmAvailable)
        {
            Assert.Inconclusive("跳过：未配置 OPENAI_API_KEY 环境变量，无法调用真实 LLM");
        }
    }

    protected IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        var zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? "";
        var searchApiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_API_KEY") ?? "";

        IsLlmAvailable = !string.IsNullOrEmpty(apiKey);

        var modelId = "deepseek-ai/DeepSeek-V3.2";
        var endpoint = "https://api.siliconflow.cn";

        services.AddApplicationServices();

        // 必须在 AddApplicationServices 之后注册，覆盖其内部的真实 UserSettingService
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
            EnableWebSearch = !string.IsNullOrEmpty(searchApiKey),
            WebSearchApiKey = searchApiKey,
            WebSearchProvider = "Tavily",
            LoadKnowledge = true,
        };
        var userSettingServiceMock = new Mock<IUserSettingService>();
        userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(testUserSetting);
        services.AddSingleton<IUserSettingService>(userSettingServiceMock.Object);

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
