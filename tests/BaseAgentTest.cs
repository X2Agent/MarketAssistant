using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// Agent Framework 测试基类
/// 环境变量必需：缺失时对应测试失败（不跳过），确保真实场景验证
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
    /// 断言真实 LLM API Key 已配置（缺失则测试失败，而非跳过）
    /// </summary>
    protected void RequireLlm()
    {
        if (!IsLlmAvailable)
        {
            Assert.Fail("OPENAI_API_KEY 环境变量未配置，无法调用真实 LLM 进行真实场景验证");
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
        var embeddingApiKey = Environment.GetEnvironmentVariable("JINA_API_KEY") ?? "";

        IsLlmAvailable = !string.IsNullOrEmpty(apiKey);

        var modelId = "deepseek-ai/DeepSeek-V3.2";
        var endpoint = "https://api.siliconflow.cn";

        services.AddApplicationServices();

        // 必须在 AddApplicationServices 之后注册，覆盖其内部的真实 UserSettingService
        var testUserSetting = new UserSetting
        {
            ProviderId = "SiliconFlow",
            ZhiTuApiToken = zhiTuApiToken,
            ModelId = modelId,
            EmbeddingModelId = "jina-embeddings-v5-text-small",
            EmbeddingEndpoint = "https://api.jina.ai",
            EmbeddingApiKey = embeddingApiKey,
            Endpoint = endpoint,
            ProviderApiKeys = new Dictionary<string, string> { ["SiliconFlow"] = apiKey },
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

        // 注册 IEmbeddingGenerator（RAG 测试依赖），通过 IEmbeddingFactory 创建
        // 仅当配置了 Jina EmbeddingApiKey 时注册，避免无密钥场景下工厂构造抛异常
        if (!string.IsNullOrEmpty(embeddingApiKey))
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            {
                var factory = sp.GetRequiredService<IEmbeddingFactory>();
                return factory.Create();
            });
        }

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
