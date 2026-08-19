using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 分析聚合器降级逻辑的确定性单测：不经过 MAF 工作流并行路由，
/// 直接驱动 <see cref="AnalysisAggregatorExecutor"/> 验证失败标记的收集、
/// 维度缺失说明的生成与全失败不派发行为。
/// </summary>
[TestClass]
public sealed class AnalysisAggregatorExecutorTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_MixedSuccessAndFailure_ShouldSendSurvivorTextsWithNote()
    {
        var (aggregator, context, sentBatches) = CreateAggregator(expectedAnalystCount: 2);

        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("FundamentalAnalyst", AnalystFailureMessages.BuildFailureText("FundamentalAnalyst", "模型超时")));
        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("NewsEventAnalyst", "正常的新闻事件分析结论"));

        Assert.AreEqual(1, sentBatches.Count, "凑齐成功+失败后应恰好派发一次");
        var payload = sentBatches[0];
        Assert.AreEqual(2, payload.Count, "载荷应包含存活文本与维度缺失说明");
        Assert.AreEqual("正常的新闻事件分析结论", payload[0].Text);
        Assert.IsTrue(payload[1].Text.StartsWith(AnalystFailureMessages.MissingDimensionNotePrefix, StringComparison.Ordinal),
            "最后一条应为维度缺失说明");
        StringAssert.Contains(payload[1].Text, "FundamentalAnalyst");
        StringAssert.Contains(payload[1].Text, "模型超时");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_AllFailed_ShouldNotDispatchCoordinator()
    {
        var (aggregator, context, sentBatches) = CreateAggregator(expectedAnalystCount: 2);

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
        var (aggregator, context, sentBatches) = CreateAggregator(expectedAnalystCount: 1);

        // 同一批次内同一作者的重复消息以最后一条为准
        await DeliverAsync(aggregator, context,
            CreateAssistantMessage("NewsEventAnalyst", "第一版结论"),
            CreateAssistantMessage("NewsEventAnalyst", "最终版结论"));

        Assert.AreEqual(1, sentBatches.Count);
        var payload = sentBatches[0];
        Assert.AreEqual(2, payload.Count, "载荷应为去重后的文本 + 维度缺失说明");
        Assert.AreEqual("最终版结论", payload[0].Text);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Collect_UserRoleMessages_ShouldBeIgnored()
    {
        var (aggregator, context, sentBatches) = CreateAggregator(expectedAnalystCount: 1);

        // 分发器广播的 user 提示词也可能到达聚合器（MAF 路由行为），必须被忽略
        await DeliverAsync(aggregator, context, new ChatMessage(ChatRole.User, "请对标的 000001 进行专业分析，提供投资建议。"));
        Assert.AreEqual(0, sentBatches.Count);

        await DeliverAsync(aggregator, context, CreateAssistantMessage("NewsEventAnalyst", "结论"));
        Assert.AreEqual(1, sentBatches.Count);
    }

    private static (AnalysisAggregatorExecutor Aggregator, IWorkflowContext Context, List<List<ChatMessage>> SentBatches)
        CreateAggregator(int expectedAnalystCount)
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

        var aggregator = new AnalysisAggregatorExecutor(
            expectedAnalystCount,
            NullLogger<AnalysisAggregatorExecutor>.Instance);

        return (aggregator, context.Object, sentBatches);
    }

    private static async Task DeliverAsync(
        AnalysisAggregatorExecutor aggregator,
        IWorkflowContext context,
        params ChatMessage[] messages)
    {
        // MessageHandler 为私有方法，通过包装类型反射调用；
        // 这里改用公共入口：MAF Executor 的消息处理经由类型路由，
        // 单测直接构造 List<ChatMessage> 并反射调用处理器。
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
