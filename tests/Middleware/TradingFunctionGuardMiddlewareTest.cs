using MarketAssistant.Agents.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Middleware;

[TestClass]
public class TradingFunctionGuardMiddlewareTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_NonPositiveMaxToolCalls_ShouldThrow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new TradingFunctionGuardMiddleware(
                NullLogger<TradingFunctionGuardMiddleware>.Instance,
                maxToolCalls: 0));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Disabled_SensitiveOperation_ShouldFailClosedAsync()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("PlaceOrderAsync");
        var nextCalled = false;

        using var run = middleware.BeginRunBudgetScope();
        var result = await middleware.InvokeAsync(
            null!,
            context,
            (_, _) =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("executed");
            },
            CancellationToken.None);

        Assert.IsFalse(nextCalled);
        Assert.IsTrue(context.Terminate);
        StringAssert.Contains(result?.ToString(), "未获授权");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task InteractiveConfirmation_MissingCallback_ShouldFailClosedAsync()
    {
        var middleware = CreateMiddleware(TradingAuthorizationMode.InteractiveConfirmation);
        var context = CreateContext("CancelOrderAsync");

        using var run = middleware.BeginRunBudgetScope();
        var result = await middleware.InvokeAsync(
            null!,
            context,
            (_, _) => ValueTask.FromResult<object?>("executed"),
            CancellationToken.None);

        Assert.IsTrue(context.Terminate);
        StringAssert.Contains(result?.ToString(), "未获授权");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task InteractiveConfirmation_Approved_ShouldExecuteAsync()
    {
        var middleware = CreateMiddleware(
            TradingAuthorizationMode.InteractiveConfirmation,
            (_, _) => Task.FromResult(true));
        var context = CreateContext("PlaceOrderAsync");
        var nextCalled = false;

        using var run = middleware.BeginRunBudgetScope();
        var result = await middleware.InvokeAsync(
            null!,
            context,
            (_, _) =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("executed");
            },
            CancellationToken.None);

        Assert.IsTrue(nextCalled);
        Assert.IsFalse(context.Terminate);
        Assert.AreEqual("executed", result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task InteractiveConfirmation_Rejected_ShouldNotExecuteAsync()
    {
        var middleware = CreateMiddleware(
            TradingAuthorizationMode.InteractiveConfirmation,
            (_, _) => Task.FromResult(false));
        var context = CreateContext("PlaceOrderAsync");
        var nextCalled = false;

        using var run = middleware.BeginRunBudgetScope();
        await middleware.InvokeAsync(
            null!,
            context,
            (_, _) =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("executed");
            },
            CancellationToken.None);

        Assert.IsFalse(nextCalled);
        Assert.IsTrue(context.Terminate);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task InteractiveConfirmation_CallbackThrows_ShouldNotExecuteAsync()
    {
        var middleware = CreateMiddleware(
            TradingAuthorizationMode.InteractiveConfirmation,
            (_, _) => throw new InvalidOperationException("confirmation unavailable"));
        var context = CreateContext("PlaceOrderAsync");
        var nextCalled = false;

        using var run = middleware.BeginRunBudgetScope();
        var result = await middleware.InvokeAsync(
            null!,
            context,
            (_, _) =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("executed");
            },
            CancellationToken.None);

        Assert.IsFalse(nextCalled);
        Assert.IsTrue(context.Terminate);
        StringAssert.Contains(result?.ToString(), "未获授权");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PreAuthorizedAutomation_SensitiveOperation_ShouldExecuteAsync()
    {
        var middleware = CreateMiddleware(TradingAuthorizationMode.PreAuthorizedAutomation);
        var context = CreateContext("PlaceOrderAsync");

        using var run = middleware.BeginRunBudgetScope();
        var result = await middleware.InvokeAsync(
            null!,
            context,
            (_, _) => ValueTask.FromResult<object?>("executed"),
            CancellationToken.None);

        Assert.IsFalse(context.Terminate);
        Assert.AreEqual("executed", result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Disabled_NonSensitiveOperation_ShouldExecuteAsync()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("GetAccountBalanceAsync");

        using var run = middleware.BeginRunBudgetScope();
        var result = await middleware.InvokeAsync(
            null!,
            context,
            (_, _) => ValueTask.FromResult<object?>("executed"),
            CancellationToken.None);

        Assert.IsFalse(context.Terminate);
        Assert.AreEqual("executed", result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ToolCallBudget_Exceeded_ShouldTerminateAsync()
    {
        var middleware = CreateMiddleware(maxToolCalls: 1);
        var firstContext = CreateContext("GetAccountBalanceAsync");
        var secondContext = CreateContext("GetAccountBalanceAsync");

        using var run = middleware.BeginRunBudgetScope();
        await middleware.InvokeAsync(
            null!,
            firstContext,
            (_, _) => ValueTask.FromResult<object?>("executed"),
            CancellationToken.None);
        var result = await middleware.InvokeAsync(
            null!,
            secondContext,
            (_, _) => ValueTask.FromResult<object?>("unexpected"),
            CancellationToken.None);

        Assert.IsTrue(secondContext.Terminate);
        StringAssert.Contains(result?.ToString(), "调用次数已达上限");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ToolCallBudget_NewRun_ShouldResetAsync()
    {
        var middleware = CreateMiddleware(maxToolCalls: 1);

        using (middleware.BeginRunBudgetScope())
        {
            var firstRunContext = CreateContext("GetAccountBalanceAsync");
            var firstResult = await middleware.InvokeAsync(
                null!,
                firstRunContext,
                (_, _) => ValueTask.FromResult<object?>("first-run"),
                CancellationToken.None);

            Assert.AreEqual("first-run", firstResult);
            Assert.IsFalse(firstRunContext.Terminate);
        }

        using (middleware.BeginRunBudgetScope())
        {
            var secondRunContext = CreateContext("GetAccountBalanceAsync");
            var secondResult = await middleware.InvokeAsync(
                null!,
                secondRunContext,
                (_, _) => ValueTask.FromResult<object?>("second-run"),
                CancellationToken.None);

            Assert.AreEqual("second-run", secondResult);
            Assert.IsFalse(secondRunContext.Terminate);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task InvokeAsync_WithoutRunScope_ShouldFailFastAsync()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("GetAccountBalanceAsync");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await middleware.InvokeAsync(
                null!,
                context,
                (_, _) => ValueTask.FromResult<object?>("unexpected"),
                CancellationToken.None));
    }

    private static TradingFunctionGuardMiddleware CreateMiddleware(
        TradingAuthorizationMode authorizationMode = TradingAuthorizationMode.Disabled,
        Func<string, string, Task<bool>>? confirmationCallback = null,
        int maxToolCalls = 20)
    {
        return new TradingFunctionGuardMiddleware(
            NullLogger<TradingFunctionGuardMiddleware>.Instance,
            authorizationMode,
            confirmationCallback,
            maxToolCalls);
    }

    private static FunctionInvocationContext CreateContext(string functionName)
    {
        var function = AIFunctionFactory.Create(
            (Func<string>)(() => "ok"),
            new AIFunctionFactoryOptions { Name = functionName });

        return new FunctionInvocationContext
        {
            Function = function,
            Arguments = new AIFunctionArguments { ["symbol"] = "BTCUSDT" }
        };
    }
}
