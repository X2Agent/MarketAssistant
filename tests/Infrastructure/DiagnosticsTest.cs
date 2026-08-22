using System.Diagnostics;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Infrastructure;

[TestClass]
public sealed class DiagnosticsTest
{
    [TestMethod]
    public void TokenTracking_ShouldAttachUsageToCurrentMarketAssistantActivity()
    {
        Activity? stoppedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MarketAssistantDiagnostics.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);
        var middleware = new TokenTrackingMiddleware(NullLogger<TokenTrackingMiddleware>.Instance);

        using (var activity = MarketAssistantDiagnostics.StartActivity("test.agent.run"))
        {
            middleware.LogAndAccumulate(
                session: null,
                inputTokens: 123,
                outputTokens: 45,
                agentName: "TestAgent",
                isPrecise: true);
        }

        Assert.IsNotNull(stoppedActivity);
        Assert.AreEqual(123L, stoppedActivity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.AreEqual(45L, stoppedActivity.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.AreEqual(true, stoppedActivity.GetTagItem("marketassistant.token_usage.precise"));
    }

    [TestMethod]
    public void RecordException_ShouldSetErrorStatusWithoutRecordingMessage()
    {
        Activity? stoppedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MarketAssistantDiagnostics.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = MarketAssistantDiagnostics.StartActivity("test.failure"))
        {
            MarketAssistantDiagnostics.RecordException(
                activity,
                new InvalidOperationException("sensitive details"));
        }

        Assert.IsNotNull(stoppedActivity);
        Assert.AreEqual(ActivityStatusCode.Error, stoppedActivity.Status);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, stoppedActivity.GetTagItem("error.type"));
        Assert.IsFalse(stoppedActivity.Tags.Any(tag => tag.Value?.Contains("sensitive details", StringComparison.Ordinal) == true));
    }
}
