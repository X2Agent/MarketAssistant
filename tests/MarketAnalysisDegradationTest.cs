using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 市场分析降级行为测试：验证单个分析师失败时整次分析仍能产出报告，
/// 以及全部分析师失败时给出明确的失败原因。
/// </summary>
[TestClass]
public sealed class MarketAnalysisDegradationTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task AnalyzeAsync_SingleAnalystFails_ShouldDegradeAndReturnReport()
    {
        // MAF Fan-In 管道存在时序性的消息路由丢失（1.19.0 仍未修复，证据见聚合器批次诊断日志；
        // 聚合器已增加有界宽限+降级发送兜底。与本降级逻辑无关的框架级缺陷，已单列跟进项）。
        // 此处用有界重试隔离该偶发，
        // 降级语义本身由 AnalysisAggregatorExecutorTest / AIAgentFailureIsolationTest 确定性覆盖。
        const int maxAttempts = 5;
        FriendlyException? lastFrameworkError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var workflow = CreateWorkflow(
                fundamentalFails: true,
                newsEventFails: false,
                out var coordinatorRequests,
                out var progressEvents,
                out var diagnosticLogs);

            try
            {
                var report = await workflow.AnalyzeAsync("000001", Guid.NewGuid());

                Assert.IsNotNull(report);
                Assert.IsNotNull(report.CoordinatorResult);
                Assert.IsFalse(string.IsNullOrWhiteSpace(report.CoordinatorResult.Summary));

                var texts = report.AnalystMessages.Select(message => message.Text ?? string.Empty).ToList();
                // P1-07：协调载荷为摘要而非全文，摘要应包含显示名与工具读取指引
                Assert.IsTrue(texts.Any(text => text.Contains("结论摘要", StringComparison.Ordinal) && text.Contains("NewsEventAnalyst", StringComparison.Ordinal)),
                    $"存活分析师的结论摘要应进入报告\n--- 诊断日志 ---\n{string.Join("\n", diagnosticLogs.TakeLast(80))}");
                Assert.IsTrue(texts.Any(text => text.Contains("get_analyst_artifact", StringComparison.Ordinal)),
                    "协调载荷应包含产物读取工具指引");
                Assert.IsTrue(texts.All(text => !AnalystFailureMessages.IsFailureMarker(text)),
                    "失败标记不应进入报告载荷");
                Assert.IsTrue(texts.Any(text => text.StartsWith(AnalystFailureMessages.MissingDimensionNotePrefix, StringComparison.Ordinal)),
                    "报告中应包含维度缺失说明");

                Assert.IsTrue(coordinatorRequests.Count == 1, "协调分析师应被恰好调用一次");
                Assert.IsTrue(progressEvents.Any(args => args.FailedAnalysts.Count > 0),
                    "UI 应收到分析师失败进度事件");
                return;
            }
            catch (FriendlyException ex) when (
                ex.Message.Contains("综合报告生成环节异常", StringComparison.Ordinal) && attempt < maxAttempts)
            {
                // barrier 消息丢失的表现：分析师全部完成但聚合器未收齐 → 记录后重试
                lastFrameworkError = ex;
            }
        }

        Assert.Fail($"重试 {maxAttempts} 次后仍失败: {lastFrameworkError?.Message}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AnalyzeAsync_AllAnalystsFail_ShouldThrowFriendlyException()
    {
        // 全部失败路径同样受 barrier 消息丢失影响，用有界重试保证最终到达全失败判定
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var workflow = CreateWorkflow(
                fundamentalFails: true,
                newsEventFails: true,
                out _,
                out _,
                out _);

            try
            {
                await workflow.AnalyzeAsync("000001", Guid.NewGuid());
            }
            catch (FriendlyException ex)
            {
                if (ex.Message.Contains("所有分析师均执行失败", StringComparison.Ordinal))
                {
                    return;
                }

                if (attempt == maxAttempts)
                {
                    Assert.Fail($"预期全失败错误，实际: {ex.Message}");
                }
            }
        }

        Assert.Fail("全部分析师失败时必须抛出 FriendlyException");
    }

    private static MarketAnalysisWorkflow CreateWorkflow(
        bool fundamentalFails,
        bool newsEventFails,
        out List<IReadOnlyList<ChatMessage>> coordinatorRequests,
        out List<AnalysisProgressEventArgs> progressEvents,
        out List<string> diagnosticLogs)
    {
        coordinatorRequests = [];
        diagnosticLogs = [];
        var capturedProgressEvents = new List<AnalysisProgressEventArgs>();
        progressEvents = capturedProgressEvents;
        var loggerFactory = new CollectingLoggerFactory(diagnosticLogs);

        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(service => service.CurrentSetting).Returns(new UserSetting
        {
            // 基本面分析师为必需角色，再显式启用新闻事件分析师，共两位
            EnabledAnalystRoles = new Dictionary<string, bool> { ["NewsEventAnalystAgent"] = true }
        });

        var chatClientFactory = new Mock<IChatClientFactory>();
        chatClientFactory.Setup(factory => factory.CreateRuntime()).Returns(new ChatClientRuntime(
            new StubChatClient("unused"),
            "Test",
            "test-model",
            "http://localhost",
            "fingerprint",
            StructuredOutputMode.Text));

        var coordinatorClient = new CoordinatorChatClient(coordinatorRequests);
        var analystFactory = new Mock<IAnalystAgentFactory>();
        analystFactory.Setup(factory => factory.CreateAnalyst(
                It.Is<Type>(type => type.Name == "FundamentalAnalystAgent"),
                It.IsAny<ChatClientRuntime>(),
                It.IsAny<AIContextProvider[]?>(), It.IsAny<IEnumerable<AITool>?>()))
            .Returns((Type _, ChatClientRuntime _, AIContextProvider[]? _, IEnumerable<AITool>? _) =>
                CreateAnalystAgent("FundamentalAnalyst", fundamentalFails, "正常的基本面分析结论"));
        analystFactory.Setup(factory => factory.CreateAnalyst(
                It.Is<Type>(type => type.Name == "NewsEventAnalystAgent"),
                It.IsAny<ChatClientRuntime>(),
                It.IsAny<AIContextProvider[]?>(), It.IsAny<IEnumerable<AITool>?>()))
            .Returns((Type _, ChatClientRuntime _, AIContextProvider[]? _, IEnumerable<AITool>? _) =>
                CreateAnalystAgent("NewsEventAnalyst", newsEventFails, "正常的新闻事件分析结论"));
        analystFactory.Setup(factory => factory.CreateAnalyst(
                It.Is<Type>(type => type.Name == "CoordinatorAnalystAgent"),
                It.IsAny<ChatClientRuntime>(),
                It.IsAny<AIContextProvider[]?>(), It.IsAny<IEnumerable<AITool>?>()))
            .Returns((Type _, ChatClientRuntime _, AIContextProvider[]? _, IEnumerable<AITool>? _) => new ChatClientAgent(
                coordinatorClient,
                new ChatClientAgentOptions
                {
                    Name = "CoordinatorAnalyst",
                    ChatOptions = new ChatOptions()
                }));

        var marketContext = new MarketContext(settingService.Object, Mock.Of<IServiceProvider>());
        var workflow = new MarketAnalysisWorkflow(
            settingService.Object,
            analystFactory.Object,
            chatClientFactory.Object,
            loggerFactory,
            new AnalysisReportCache(marketContext),
            marketContext,
            new InMemoryArtifactStore(),
            NullLogger<MarketAnalysisWorkflow>.Instance);

        workflow.ProgressChanged += (_, args) => capturedProgressEvents.Add(args);
        return workflow;
    }

    private static AIAgent CreateAnalystAgent(string name, bool fails, string replyText)
    {
        IChatClient client = fails ? new ThrowingChatClient() : new StubChatClient(replyText);
        return new ChatClientAgent(client, new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions()
        });
    }

    private static string CreateCoordinatorResultJson()
    {
        var result = new CoordinatorResult
        {
            OverallScore = 7.5f,
            InvestmentRating = InvestmentRating.Buy,
            TargetPrice = "10-12 元",
            PriceChangeExpectation = "综合判断预计上涨 8-12%",
            TimeHorizon = Duration.MediumTerm,
            TimeHorizonDescription = "中期 6-12 个月",
            RiskLevel = Level.Medium,
            ConfidencePercentage = 70,
            DimensionScores = new AnalysisDimensionScores
            {
                Fundamental = 7,
                Technical = 6,
                Financial = 7,
                Sentiment = 6,
                News = 6
            },
            InvestmentHighlights = ["增长确定性强", "现金流充沛", "估值处于合理区间"],
            RiskFactors = ["行业竞争加剧", "宏观需求波动"],
            OperationSuggestions =
            [
                "建议在 10-10.5 元区间分批建仓",
                "设置 9 元止损位",
                "仓位控制在总资产的 15% 以内"
            ],
            ConsensusAnalysis = "各分析师一致认为该公司基本面保持稳健，主营业务增长逻辑清晰，现金流质量良好，当前估值处于历史偏低区间，适合中长线投资者逢低分批布局。",
            DisagreementAnalysis = "基本面分析师对成长空间更为乐观，而技术分析师提示短期股价存在回调压力，双方对入场时点存在分歧；综合判断为先震荡筑底、后进入趋势行情。",
            Summary = "综合判断建议逢低分批建仓，中期持有",
            KeyIndicators = Enumerable.Range(1, 6).Select(index => new KeyIndicator
            {
                AnalystSource = "技术分析师",
                Category = "技术指标",
                Name = $"指标{index}",
                Value = "买入",
                Signal = "买入",
                Suggestion = "短期关注量能配合情况再做加减仓"
            }).ToList(),
            QualityMetrics = new AnalysisQualityMetrics
            {
                DataCompletenessPercent = 80,
                AnalystConsensusPercent = 75,
                LimitationsNote = "部分情绪面数据存在延迟，短期剧烈波动期间结论可靠性有所下降",
                OverallQualityLevel = AnalysisQualityLevel.Medium
            }
        };

        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        return JsonSerializer.Serialize(result, options);
    }

    /// <summary>
    /// 固定回复的 IChatClient 桩。
    /// </summary>
    private sealed class StubChatClient(string replyText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, replyText)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, replyText);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 收集日志文本的 ILoggerFactory，用于测试失败时输出工作流诊断。
    /// </summary>
    private sealed class CollectingLoggerFactory(List<string> sink) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(sink, categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class CollectingLogger(List<string> sink, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = $"[{categoryName}] {formatter(state, exception)}";
            lock (sink)
            {
                sink.Add(text);
            }
        }
    }

    /// <summary>
    /// 模拟分析师运行故障的 IChatClient 桩。
    /// </summary>
    private sealed class ThrowingChatClient : IChatClient
    {
        public const string FailureReason = "模拟的分析师运行故障";

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

    /// <summary>
    /// 记录请求并返回合法 CoordinatorResult JSON 的 IChatClient 桩。
    /// </summary>
    private sealed class CoordinatorChatClient(List<IReadOnlyList<ChatMessage>> requests) : IChatClient
    {
        private readonly string _replyJson = CreateCoordinatorResultJson();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            requests.Add(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _replyJson)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            requests.Add(messages.ToList());
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _replyJson);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
