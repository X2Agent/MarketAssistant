using MarketAssistant.Agents.Middleware;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace TestMarketAssistant.Middleware;

[TestClass]
public class TradingFunctionGuardMiddlewareTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_DefaultMaxToolCalls_ShouldBe20()
    {
        var middleware = new TradingFunctionGuardMiddleware(
            NullLogger<TradingFunctionGuardMiddleware>.Instance);

        var maxToolCalls = GetPrivateField<int>(middleware, "_maxToolCalls");

        Assert.AreEqual(20, maxToolCalls);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_CustomMaxToolCalls_ShouldAccept()
    {
        var middleware = new TradingFunctionGuardMiddleware(
            NullLogger<TradingFunctionGuardMiddleware>.Instance,
            maxToolCalls: 5);

        var maxToolCalls = GetPrivateField<int>(middleware, "_maxToolCalls");

        Assert.AreEqual(5, maxToolCalls);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ResetCallCount_ShouldResetToZero()
    {
        var middleware = new TradingFunctionGuardMiddleware(
            NullLogger<TradingFunctionGuardMiddleware>.Instance);

        var countField = typeof(TradingFunctionGuardMiddleware).GetField(
            "_toolCallCount",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        countField.SetValue(middleware, 12);

        middleware.ResetCallCount();

        Assert.AreEqual(0, countField.GetValue(middleware));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ConfirmationCallback_DefaultNull()
    {
        var middleware = new TradingFunctionGuardMiddleware(
            NullLogger<TradingFunctionGuardMiddleware>.Instance);

        Assert.IsNull(middleware.ConfirmationCallback);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found.");
        return (T)field!.GetValue(instance)!;
    }
}
