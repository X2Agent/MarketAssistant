using MarketAssistant.Agents.TokenManagement;

namespace TestMarketAssistant.Middleware;

/// <summary>
/// <see cref="TokenUsageRecorder"/> 单元测试：验证按角色归集、作用域嵌套/释放语义，
/// 以及 AsyncLocal 沿异步链路（含并发子任务）向下传递的关键假设。
/// </summary>
[TestClass]
public class TokenUsageRecorderTest
{
    private static readonly Guid RunId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    [TestMethod]
    [TestCategory("Unit")]
    public void CurrentScope_WithoutBeginScope_ShouldBeNull()
    {
        var recorder = new TokenUsageRecorder();

        Assert.IsNull(recorder.CurrentScope);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BeginScope_ShouldExposeScopeWithRunId()
    {
        var recorder = new TokenUsageRecorder();

        using var scope = recorder.BeginScope(RunId);

        Assert.AreSame(scope, recorder.CurrentScope);
        Assert.AreEqual(RunId, scope.RunId);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Dispose_ShouldClearCurrentScope()
    {
        var recorder = new TokenUsageRecorder();
        var scope = recorder.BeginScope(RunId);

        scope.Dispose();

        Assert.IsNull(recorder.CurrentScope);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Report_ShouldAttributeTokensPerAgentAndAggregateTotals()
    {
        var recorder = new TokenUsageRecorder();
        using var scope = recorder.BeginScope(RunId);

        scope.Report("基本面分析师", 100, 50, isPrecise: true);
        scope.Report("技术面分析师", 200, 80, isPrecise: true);
        scope.Report("基本面分析师", 10, 5, isPrecise: true);

        var usage = scope.Snapshot();

        Assert.AreEqual(310L, usage.InputTokens);
        Assert.AreEqual(135L, usage.OutputTokens);
        Assert.AreEqual(3, usage.CallCount);
        Assert.AreEqual(445L, usage.TotalTokens);
        Assert.IsFalse(usage.IsEstimated);
        Assert.IsTrue(usage.HasValue);
        Assert.HasCount(2, usage.Entries);

        var fundamental = usage.EntryFor("基本面分析师");
        Assert.IsNotNull(fundamental);
        Assert.AreEqual(110L, fundamental.InputTokens);
        Assert.AreEqual(55L, fundamental.OutputTokens);
        Assert.AreEqual(2, fundamental.CallCount);
        Assert.AreEqual(165L, fundamental.TotalTokens);

        Assert.AreEqual((110L, 55L), scope.TokensFor("基本面分析师"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TokensFor_UnknownOrBlankAgent_ShouldReturnZeros()
    {
        var recorder = new TokenUsageRecorder();
        using var scope = recorder.BeginScope(RunId);
        scope.Report("基本面分析师", 100, 50, isPrecise: true);

        Assert.AreEqual((0L, 0L), scope.TokensFor("不存在的角色"));
        Assert.AreEqual((0L, 0L), scope.TokensFor(null));
        Assert.AreEqual((0L, 0L), scope.TokensFor(""));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Report_WithBlankAgentName_ShouldAggregateUnderUnknown()
    {
        var recorder = new TokenUsageRecorder();
        using var scope = recorder.BeginScope(RunId);

        scope.Report(null, 10, 1, isPrecise: true);
        scope.Report("   ", 20, 2, isPrecise: true);

        var unknown = scope.Snapshot().EntryFor(TokenUsageRecorder.UnknownAgentName);

        Assert.IsNotNull(unknown);
        Assert.AreEqual(30L, unknown.InputTokens);
        Assert.AreEqual(3L, unknown.OutputTokens);
        Assert.AreEqual(2, unknown.CallCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Report_WithNonPositiveTokens_ShouldBeIgnored()
    {
        var recorder = new TokenUsageRecorder();
        using var scope = recorder.BeginScope(RunId);

        scope.Report("基本面分析师", 0, 0, isPrecise: true);
        scope.Report("基本面分析师", -5, -5, isPrecise: true);

        var usage = scope.Snapshot();

        Assert.AreEqual(0L, usage.InputTokens);
        Assert.AreEqual(0, usage.CallCount);
        Assert.IsFalse(usage.HasValue);
        Assert.IsEmpty(usage.Entries);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Report_WithEstimatedTokens_ShouldFlagScopeAndEntry()
    {
        var recorder = new TokenUsageRecorder();
        using var scope = recorder.BeginScope(RunId);

        scope.Report("基本面分析师", 100, 50, isPrecise: true);
        Assert.IsFalse(scope.Snapshot().IsEstimated);

        scope.Report("基本面分析师", 10, 5, isPrecise: false);

        var usage = scope.Snapshot();
        Assert.IsTrue(usage.IsEstimated, "任一次估算都应把整体标记为不精确");
        var entry = usage.EntryFor("基本面分析师");
        Assert.IsNotNull(entry);
        Assert.IsTrue(entry.IsEstimated);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Report_AfterDispose_ShouldBeDropped()
    {
        var recorder = new TokenUsageRecorder();
        var scope = recorder.BeginScope(RunId);
        scope.Report("基本面分析师", 100, 50, isPrecise: true);

        scope.Dispose();
        scope.Report("基本面分析师", 1_000_000, 1_000_000, isPrecise: true);

        var usage = scope.Snapshot();
        Assert.AreEqual(100L, usage.InputTokens);
        Assert.AreEqual(1, usage.CallCount, "作用域释放后不得再接受写入");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NestedScopes_ShouldIsolateTokensAndRestoreParent()
    {
        var recorder = new TokenUsageRecorder();

        using var outer = recorder.BeginScope(Guid.NewGuid());
        outer.Report("基本面分析师", 100, 50, isPrecise: true);

        var inner = recorder.BeginScope(Guid.NewGuid());
        Assert.AreSame(inner, recorder.CurrentScope);
        inner.Report("技术面分析师", 7, 3, isPrecise: true);
        Assert.HasCount(1, inner.Snapshot().Entries);

        inner.Dispose();
        Assert.AreSame(outer, recorder.CurrentScope, "释放内层作用域应还原父作用域");

        outer.Report("基本面分析师", 1, 1, isPrecise: true);

        var outerUsage = outer.Snapshot();
        Assert.AreEqual(101L, outerUsage.InputTokens, "内层作用域的 Token 不得回流到外层");
        Assert.AreEqual(2, outerUsage.CallCount);
        Assert.IsNull(outerUsage.EntryFor("技术面分析师"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Snapshot_ShouldBeIndependentCopy()
    {
        var recorder = new TokenUsageRecorder();
        using var scope = recorder.BeginScope(RunId);
        scope.Report("基本面分析师", 100, 50, isPrecise: true);

        var snapshot = scope.Snapshot();
        scope.Report("基本面分析师", 100, 50, isPrecise: true);

        Assert.AreEqual(100L, snapshot.InputTokens);
        Assert.AreEqual(1, snapshot.CallCount);
        var entry = snapshot.EntryFor("基本面分析师");
        Assert.IsNotNull(entry);
        Assert.AreEqual(100L, entry.InputTokens);
        Assert.AreEqual(1, entry.CallCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Scope_ShouldFlowIntoChildTasks()
    {
        // MAF 工作流内的 Agent 中间件在子任务中执行，必须能通过 AsyncLocal 看到发起运行的作用域
        var recorder = new TokenUsageRecorder();
        using var scope = recorder.BeginScope(RunId);

        var observed = await Task.Run(() => recorder.CurrentScope);

        Assert.AreSame(scope, observed);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ConcurrentScopes_ShouldNotLeakTokensAcrossRuns()
    {
        var recorder = new TokenUsageRecorder();

        static async Task<long> RunAsync(TokenUsageRecorder recorder, long tokensPerCall)
        {
            using var scope = recorder.BeginScope(Guid.NewGuid());
            await Task.Delay(5).ConfigureAwait(false);
            for (var i = 0; i < 10; i++)
            {
                await Task.Run(() => scope.Report("分析师", tokensPerCall, tokensPerCall, isPrecise: true))
                    .ConfigureAwait(false);
            }

            var usage = scope.Snapshot();
            Assert.AreEqual(10, usage.CallCount);
            Assert.HasCount(1, usage.Entries);
            return usage.InputTokens;
        }

        var results = await Task.WhenAll(RunAsync(recorder, 1), RunAsync(recorder, 2), RunAsync(recorder, 3));

        Assert.AreEqual(10L, results[0], "并发运行之间不得串号");
        Assert.AreEqual(20L, results[1]);
        Assert.AreEqual(30L, results[2]);
    }
}
