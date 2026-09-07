using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 分析聚合器降级逻辑的确定性单测：不经过 MAF 工作流并行路由，
/// 直接驱动 <see cref="AnalysisAggregatorExecutor"/> 验证失败标记的收集、
/// 维度缺失说明、产物摘要化（P1-07）与全失败不派发行为。
/// </summary>
[TestClass]
public sealed class AnalysisAggregatorExecutorTest
{
    private static readonly Guid TestRunId = Guid.NewGuid();
    private MarketAssistant.Services.Agents.MarketAnalysis.Artifacts.FileAnalystArtifactStore Store { get; set; } = null!;
    private readonly string ArtifactRoot =
        Path.Combine(Path.GetTempPath(), "aggregator-test-" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_MixedSuccessAndFailure_ShouldSendSummariesWithNote()
    {
        var (aggregator, context, sentBatches, store) = CreateAggregator(expectedAnalystCount: 2);

        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("FundamentalAnalyst", AnalystFailureMessages.BuildFailureText("FundamentalAnalyst", "模型超时")));
        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("NewsEventAnalyst", "正常的新闻事件分析结论"));

        Assert.AreEqual(1, sentBatches.Count, "凑齐成功+失败后应恰好派发一次");
        var payload = sentBatches[0];
        // 载荷：系统指引 + 成功分析师摘要 + 维度缺失说明（P1-07 摘要化后多一条系统指引）
        Assert.AreEqual(3, payload.Count);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agg-debug.txt"), payload.Select(m => $"author={m.AuthorName} text={m.Text}"));
        StringAssert.Contains(payload[0].Text, "get_analyst_artifact", "首条应为工具使用指引");
        StringAssert.Contains(payload[1].Text, "【NewsEventAnalyst】结论摘要", "成功条目应为摘要而非全文");
        StringAssert.Contains(payload[1].Text, TestRunId.ToString("N"), "摘要应携带 runId 参数");
        StringAssert.Contains(payload[2].Text, AnalystFailureMessages.MissingDimensionNotePrefix);
        StringAssert.Contains(payload[2].Text, "FundamentalAnalyst");
        StringAssert.Contains(payload[2].Text, "模型超时");

        // 全文应已落盘，可按 runId+analyst 读回
        var artifact = await store.GetAsync(TestRunId, "NewsEventAnalyst");
        StringAssert.Contains(artifact, "正常的新闻事件分析结论");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_AllFailed_ShouldNotDispatchCoordinator()
    {
        var (aggregator, context, sentBatches, store) = CreateAggregator(expectedAnalystCount: 2);

        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("FundamentalAnalyst", AnalystFailureMessages.BuildFailureText("FundamentalAnalyst", "错误A")));
        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("NewsEventAnalyst", AnalystFailureMessages.BuildFailureText("NewsEventAnalyst", "错误B")));

        Assert.AreEqual(0, sentBatches.Count, "全部失败时不得派发协调分析师（由工作流终局诊断报错）");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_DuplicateAuthorInSameBatch_ShouldKeepLastMessage()
    {
        var (aggregator, context, sentBatches, store) = CreateAggregator(expectedAnalystCount: 1);

        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("NewsEventAnalyst", "第一版结论"),
            CreateAssistantMessage("NewsEventAnalyst", "最终版结论"));

        Assert.AreEqual(1, sentBatches.Count);
        var payload = sentBatches[0];
        Assert.AreEqual(2, payload.Count, "无失败时载荷应为指引 + 去重后的摘要");
        StringAssert.Contains(payload[1].Text, "最终版结论".Length.ToString(), "摘要字符数应对应去重后的最后一条");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_UserRoleMessages_ShouldBeIgnored()
    {
        var (aggregator, context, sentBatches, store) = CreateAggregator(expectedAnalystCount: 1);

        await DeliverAsync(aggregator, context, new ChatMessage(ChatRole.User, "请对标的 000001 进行专业分析，提供投资建议。"));
        Assert.AreEqual(0, sentBatches.Count);

        await DeliverAsync(aggregator, context, CreateAssistantMessage("NewsEventAnalyst", "结论"));
        Assert.AreEqual(1, sentBatches.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_IncompleteAfterGrace_ShouldDegradeSend()
    {
        var (aggregator, context, sentBatches, store) = CreateAggregator(
            expectedAnalystCount: 2, incompleteGrace: TimeSpan.FromMilliseconds(200));

        // 仅一位分析师完成，另一位消息因 barrier 缺陷丢失
        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("NewsEventAnalyst", "仅一位分析师完成的结论"));

        Assert.AreEqual(0, sentBatches.Count, "宽限期内不得派发");

        await Task.Delay(600);
        Assert.AreEqual(1, sentBatches.Count, "宽限期后应降级派发已有结果");
        var payload = sentBatches[0];
        StringAssert.Contains(payload[0].Text, "不完整", "首条应为降级说明");
        StringAssert.Contains(payload[1].Text, "get_analyst_artifact", "随后应为工具使用指引");
        StringAssert.Contains(payload[2].Text, "【NewsEventAnalyst】结论摘要");

        var artifact = await store.GetAsync(TestRunId, "NewsEventAnalyst");
        StringAssert.Contains(artifact, "仅一位分析师完成的结论");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_IncompleteThenCompleteWithinGrace_ShouldSendOnlyOnce()
    {
        var (aggregator, context, sentBatches, _) = CreateAggregator(
            expectedAnalystCount: 2, incompleteGrace: TimeSpan.FromSeconds(10));

        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("NewsEventAnalyst", "先到的结论"));
        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("FundamentalAnalyst", "补齐的结论"));

        Assert.AreEqual(1, sentBatches.Count, "宽限期内收齐应走正常路径且只派发一次");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(ArtifactRoot))
        {
            Directory.Delete(ArtifactRoot, recursive: true);
        }
    }

    private static (AnalysisAggregatorExecutor Aggregator, IWorkflowContext Context, List<List<ChatMessage>> SentBatches, MarketAssistant.Services.Agents.MarketAnalysis.Artifacts.IAnalystArtifactStore Store)
        CreateAggregator(int expectedAnalystCount, TimeSpan? incompleteGrace = null)
    {
        var sentBatches = new List<List<ChatMessage>>();
        var context = new Mock<IWorkflowContext>();
        context
            .Setup(workflowContext => workflowContext.SendMessageAsync(
                It.IsAny<object>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<object, string?, CancellationToken>((message, _, _) =>
            {
                if (message is List<ChatMessage> batch)
                {
                    sentBatches.Add(batch);
                }
            })
            .Returns(ValueTask.CompletedTask);

        var instance = new AnalysisAggregatorExecutorTest();
        instance.Store = new MarketAssistant.Services.Agents.MarketAnalysis.Artifacts.FileAnalystArtifactStore(instance.ArtifactRoot);
        var aggregator = new AnalysisAggregatorExecutor(
            expectedAnalystCount,
            TestRunId,
            instance.Store,
            new Dictionary<string, string>(),
            NullLoggerFactory.Instance.CreateLogger<MarketAssistant.Agents.MarketAnalysis.Executors.AnalysisAggregatorExecutor>(),
            incompleteGrace ?? TimeSpan.FromMinutes(5));

        return (aggregator, context.Object, sentBatches, instance.Store);
    }

    private static async Task DeliverAsync(
        AnalysisAggregatorExecutor aggregator,
        IWorkflowContext context,
        params ChatMessage[] messages)
    {
        // MessageHandler 为私有方法，通过反射调用（与 MAF 类型路由行为一致）
        var method = typeof(AnalysisAggregatorExecutor)
            .GetMethod("HandleAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method, "私有 MessageHandler HandleAsync 应存在");
        var task = (ValueTask)method!.Invoke(aggregator, [messages.ToList(), context, CancellationToken.None])!;
        await task;
    }

    private static ChatMessage CreateAssistantMessage(string author, string text)
        => new(ChatRole.Assistant, text)
        {
            AuthorName = author
        };
}
