using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Agents.Analysts;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant;

/// <summary>
/// 分析师中间件管道顺序契约回归测试：
/// ChatClientAgent → TokenTracking → FailureIsolation（最外层）。
/// 通过 <see cref="AnalystAgentFactory.CreateAnalyst"/> 传入失败回调时，
/// 内层异常（含 Token 熔断同源异常）必须被转换为失败标记消息而非抛出；
/// 未传回调（协调器语义）时异常必须原样向上传播。
/// 顺序契约若被破坏（失败隔离包进 TokenTracking 内层），本测试将失败。
/// </summary>
[TestClass]
public sealed class AnalystAgentFactoryMiddlewareOrderTest : BaseAgentTest
{
    private const string FailureReason = "模拟的分析师运行故障";

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // AgentSkillsProvider 在测试输出目录下通常无 skills 内容文件；
        // 预建空目录，确保技能扫描在无技能场景下安全返回
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "skills"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CreateAnalyst_WithFailureCallback_InnerExceptionBecomesMarkerMessage()
    {
        var factory = CreateFactory();
        var runtime = CreateThrowingRuntime();
        var failures = new List<Exception>();

        var agent = factory.CreateAnalyst(
            typeof(FinancialAnalystAgent), runtime, onAnalysisFailed: failures.Add);

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]);

        Assert.HasCount(1, failures, "失败回调应恰好被触发一次");
        Assert.IsTrue(AnalystFailureMessages.IsFailureMarker(response.Text),
            $"响应应为失败标记消息，实际: {response.Text}");
        Assert.Contains("FinancialAnalyst", response.Text);
        Assert.Contains(FailureReason, response.Text);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CreateAnalyst_WithoutFailureCallback_ExceptionPropagates()
    {
        var factory = CreateFactory();
        var runtime = CreateThrowingRuntime();

        var agent = factory.CreateAnalyst(typeof(FinancialAnalystAgent), runtime);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => agent.RunAsync([new ChatMessage(ChatRole.User, "分析 600519")]));

        Assert.Contains(FailureReason, exception.Message);
    }

    private AnalystAgentFactory CreateFactory()
    {
        return new AnalystAgentFactory(
            _serviceProvider,
            _serviceProvider.GetRequiredService<MarketContext>(),
            _serviceProvider.GetRequiredService<AgentSkillsProvider>(),
            _serviceProvider.GetRequiredService<AnalystPromptLoader>(),
            _serviceProvider.GetRequiredService<TokenTrackingMiddleware>(),
            _serviceProvider.GetRequiredService<ILogger<AnalystAgentFactory>>());
    }

    private static ChatClientRuntime CreateThrowingRuntime()
    {
        return new ChatClientRuntime(
            new ThrowingChatClient(),
            "UnitTest",
            "unit-test-model",
            "https://unittest.local",
            "unit-test-fingerprint",
            StructuredOutputMode.JsonObject);
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(FailureReason);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(FailureReason);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
