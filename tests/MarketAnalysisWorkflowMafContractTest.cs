using System.Runtime.CompilerServices;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// MAF 框架契约回归测试：锁定 <see cref="MarketAnalysisWorkflow"/> 依赖的框架内部行为，
/// 升级 Microsoft.Agents.AI 版本时必须先通过本测试，防止已知规避点静默失效：
/// 1. AIAgent 在工作流中按「ChatMessage 转发 → TurnToken 真实执行」两阶段被调用，
///    进度计数只统计 TurnToken 阶段——若两阶段合并，转发阶段假完成会使完成计数超过总数；
/// 2. ExecutorId 由 Agent ASCII Name 经 GetDescriptiveId 派生（Name + "_" + Id），
///    工作流的名称 → 显示名映射按该前缀回查——若清洗规则变化，映射会静默断裂。
/// </summary>
[TestClass]
public sealed partial class MarketAnalysisWorkflowMafContractTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task Workflow_AgentExecutorId_ShouldStartWithAsciiAgentName()
    {
        var agent = new ChatClientAgent(new StubChatClient("分析结论"), new ChatClientAgentOptions
        {
            Name = "TechnicalAnalyst",
            ChatOptions = new ChatOptions()
        });
        var dispatcher = new TurnTokenDispatcher();
        var builder = new WorkflowBuilder(dispatcher);
        builder.AddEdge(dispatcher, agent);
        builder.WithOutputFrom(agent);

        await using var run = await InProcessExecution.RunStreamingAsync(builder.Build(), "600519");

        var agentExecutorIds = new List<string>();
        await foreach (var evt in run.WatchStreamAsync())
        {
            if (evt is ExecutorInvokedEvent invoked && invoked.ExecutorId != "TurnTokenDispatcher")
            {
                agentExecutorIds.Add(invoked.ExecutorId);
            }
        }

        Assert.IsTrue(agentExecutorIds.Count > 0, "应观察到 Agent Executor 的调用事件");
        Assert.IsTrue(
            agentExecutorIds.All(id => id.StartsWith("TechnicalAnalyst", StringComparison.Ordinal)),
            $"ExecutorId 应以 ASCII Agent Name 为前缀（GetDescriptiveId 契约），实际: [{string.Join(", ", agentExecutorIds)}]");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AnalyzeAsync_ProgressInvariants_ShouldHoldAcrossTurnTokenPhases()
    {
        // Fan-In barrier 存在偶发消息路由丢失（见 MarketAnalysisDegradationTest 说明），
        // 按同一惯例用有界重试隔离该偶发；契约本身由断言确定性覆盖
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var workflow = CreateWorkflowWithTwoSucceedingAnalysts(out var progressEvents);

            try
            {
                // 成功返回报告本身即守护 WithOutputFrom auto-yield 流式时序规避点：
                // 若 WorkflowOutputEvent 丢失，AnalyzeAsync 会抛「未收到输出」异常
                var report = await workflow.AnalyzeAsync("000001", Guid.NewGuid());
                Assert.IsNotNull(report.CoordinatorResult);

                var inProgress = progressEvents.Where(e => e.IsInProgress && e.TotalAnalysts > 0).ToList();
                Assert.IsTrue(inProgress.Count > 0, "应发布带分析师进度的进行中事件");
                Assert.IsTrue(
                    inProgress.All(e => e.CompletedAnalysts <= e.TotalAnalysts),
                    "消息转发阶段的假完成不得使完成计数超过总数（TurnToken 两阶段契约失效）");
                Assert.Contains(e => e.CompletedAnalysts == e.TotalAnalysts, inProgress,
                    "应存在全部分析师完成（且未超计）的进度事件");

                var completedCounts = inProgress.Select(e => e.CompletedAnalysts).ToList();
                Assert.IsTrue(
                    completedCounts.SequenceEqual([.. completedCounts.Order()]),
                    $"完成计数应单调不减，实际: [{string.Join(", ", completedCounts)}]");

                var terminal = progressEvents.Last(e => !e.IsInProgress);
                Assert.IsTrue(
                    terminal.Analysts.All(s => s.Status == AnalystRunStatus.Completed),
                    $"终态所有分析师应为 Completed，实际: {string.Join(", ", terminal.Analysts.Select(s => $"{s.DisplayName}={s.Status}"))}");
                return;
            }
            catch (FriendlyException ex) when (
                ex.Message.Contains("综合报告生成环节异常", StringComparison.Ordinal) && attempt < maxAttempts)
            {
            }
        }

        Assert.Fail($"重试 {maxAttempts} 次后仍受框架 barrier 偶发缺陷影响，无法验证进度契约");
    }

    private static MarketAnalysisWorkflow CreateWorkflowWithTwoSucceedingAnalysts(
        out List<AnalysisProgressEventArgs> progressEvents)
    {
        var captured = new List<AnalysisProgressEventArgs>();
        progressEvents = captured;

        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(service => service.CurrentSetting).Returns(new UserSetting
        {
            // 基本面分析师为必需角色，再显式启用新闻事件分析师，共两位且均成功
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

        var coordinatorClient = new CoordinatorChatClient();
        var analystFactory = new Mock<IAnalystAgentFactory>();
        analystFactory.Setup(factory => factory.CreateAnalyst(
                It.Is<Type>(type => type.Name == "FundamentalAnalystAgent"),
                It.IsAny<ChatClientRuntime>(),
                It.IsAny<AIContextProvider[]?>(), It.IsAny<IEnumerable<AITool>?>(),
                It.IsAny<Action<Exception>?>()))
            .Returns((Type _, ChatClientRuntime _, AIContextProvider[]? _, IEnumerable<AITool>? _, Action<Exception>? onAnalysisFailed) =>
                CreateSucceedingAnalyst("FundamentalAnalyst", "正常的基本面分析结论", onAnalysisFailed));
        analystFactory.Setup(factory => factory.CreateAnalyst(
                It.Is<Type>(type => type.Name == "NewsEventAnalystAgent"),
                It.IsAny<ChatClientRuntime>(),
                It.IsAny<AIContextProvider[]?>(), It.IsAny<IEnumerable<AITool>?>(),
                It.IsAny<Action<Exception>?>()))
            .Returns((Type _, ChatClientRuntime _, AIContextProvider[]? _, IEnumerable<AITool>? _, Action<Exception>? onAnalysisFailed) =>
                CreateSucceedingAnalyst("NewsEventAnalyst", "正常的新闻事件分析结论", onAnalysisFailed));
        analystFactory.Setup(factory => factory.CreateAnalyst(
                It.Is<Type>(type => type.Name == "CoordinatorAnalystAgent"),
                It.IsAny<ChatClientRuntime>(),
                It.IsAny<AIContextProvider[]?>(), It.IsAny<IEnumerable<AITool>?>(),
                It.IsAny<Action<Exception>?>()))
            .Returns((Type _, ChatClientRuntime _, AIContextProvider[]? _, IEnumerable<AITool>? _, Action<Exception>? _) => new ChatClientAgent(
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
            NullLoggerFactory.Instance,
            new AnalysisReportCache(marketContext),
            marketContext,
            new InMemoryArtifactStore(),
            NullLogger<MarketAnalysisWorkflow>.Instance);

        // 进度事件由 Executor 线程与事件循环并发发布，收集端自行保证线程安全
        workflow.ProgressChanged += (_, args) =>
        {
            lock (captured)
            {
                captured.Add(args);
            }
        };
        return workflow;
    }

    private static AIAgent CreateSucceedingAnalyst(string name, string replyText, Action<Exception>? onAnalysisFailed)
    {
        // 与真实 AnalystAgentFactory 行为对齐：传回调的分析师附带失败隔离包装
        return new ChatClientAgent(new StubChatClient(replyText), new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions()
        }).WithFailureIsolation(onAnalysisFailed);
    }

    /// <summary>
    /// 复刻 AnalysisDispatcherExecutor 的最小分发协议：广播 ChatMessage 后广播 TurnToken。
    /// </summary>
    [SendsMessage(typeof(ChatMessage))]
    [SendsMessage(typeof(TurnToken))]
    private sealed partial class TurnTokenDispatcher() : Executor("TurnTokenDispatcher")
    {
        [MessageHandler]
        private async ValueTask HandleAsync(
            string assetSymbol,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            await context.SendMessageAsync(
                new ChatMessage(ChatRole.User, $"请对标的 {assetSymbol} 进行专业分析"),
                cancellationToken);
            await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken);
        }
    }

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
    /// 返回合法 CoordinatorResult JSON 的 IChatClient 桩（JSON 复用降级测试的构造器）。
    /// </summary>
    private sealed class CoordinatorChatClient : IChatClient
    {
        private readonly string _replyJson = MarketAnalysisDegradationTest.CreateCoordinatorResultJson();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _replyJson)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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
