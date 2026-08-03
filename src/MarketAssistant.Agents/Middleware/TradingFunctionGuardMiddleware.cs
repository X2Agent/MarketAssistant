using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace MarketAssistant.Agents.Middleware;

/// <summary>
/// 交易敏感操作的授权模式。
/// </summary>
public enum TradingAuthorizationMode
{
    /// <summary>
    /// 禁止执行任何真实下单或撤单操作。
    /// </summary>
    Disabled,

    /// <summary>
    /// 每个敏感操作均需通过外部确认回调授权。
    /// </summary>
    InteractiveConfirmation,

    /// <summary>
    /// 调用方已在进入 Agent 前完成自动交易策略与风控校验。
    /// </summary>
    PreAuthorizedAutomation
}

/// <summary>
/// 交易工具调用守卫中间件，拦截 Agent 的函数调用实现：
/// 1. 敏感操作（PlaceOrder/CancelOrder）审计日志
/// 2. 单次 Agent Run 的工具调用计数限制
/// 3. 根据显式授权模式放行或拒绝敏感操作
/// </summary>
public sealed class TradingFunctionGuardMiddleware
{
    private const int DefaultMaxToolCalls = 20;

    private readonly ILogger _logger;
    private readonly int _maxToolCalls;
    private readonly TradingAuthorizationMode _authorizationMode;
    private readonly Func<string, string, Task<bool>>? _confirmationCallback;
    private readonly AsyncLocal<RunBudgetState?> _currentRunBudget = new();

    /// <param name="logger">日志记录器。</param>
    /// <param name="authorizationMode">敏感交易操作授权模式。</param>
    /// <param name="confirmationCallback">交互确认回调；交互模式下未提供时严格拒绝。</param>
    /// <param name="maxToolCalls">单次 Agent Run 最大工具调用次数，防止无限循环（默认 20）。</param>
    public TradingFunctionGuardMiddleware(
        ILogger<TradingFunctionGuardMiddleware> logger,
        TradingAuthorizationMode authorizationMode = TradingAuthorizationMode.Disabled,
        Func<string, string, Task<bool>>? confirmationCallback = null,
        int maxToolCalls = DefaultMaxToolCalls)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxToolCalls);

        _logger = logger;
        _authorizationMode = authorizationMode;
        _confirmationCallback = confirmationCallback;
        _maxToolCalls = maxToolCalls;
    }

    /// <summary>
    /// 非流式 Agent Run 边界，为本轮工具调用建立独立预算。
    /// </summary>
    public async Task<AgentResponse> InvokeRunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        using var scope = BeginRunBudgetScope();
        return await innerAgent.RunAsync(messages, session, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 流式 Agent Run 边界，为本轮工具调用建立独立预算。
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> InvokeRunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var scope = BeginRunBudgetScope();
        await foreach (var update in innerAgent
                           .RunStreamingAsync(messages, session, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Function Calling 中间件入口，通过 agent.AsBuilder().Use(this.InvokeAsync).Build() 附加。
    /// </summary>
    public async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var functionName = context.Function.Name;
        var runBudget = _currentRunBudget.Value
            ?? throw new InvalidOperationException(
                "交易工具守卫缺少 Agent Run 上下文，请同时注册 Run 和 Function 中间件");
        var callIndex = Interlocked.Increment(ref runBudget.ToolCallCount);

        // 1. 调用计数守卫
        if (callIndex > _maxToolCalls)
        {
            _logger.LogWarning(
                "TradingAgent 工具调用次数 {Count} 超过上限 {Max}，终止调用链",
                callIndex, _maxToolCalls);
            context.Terminate = true;
            return $"错误：工具调用次数已达上限 {_maxToolCalls}，请直接给出结论。";
        }

        // 2. 敏感操作审计 + Human-in-the-Loop
        if (IsSensitiveOperation(functionName))
        {
            var args = FormatArguments(context);

            _logger.LogInformation(
                "交易敏感操作拦截 [{Function}] 参数字段: {ArgumentNames}，授权模式: {AuthorizationMode} (第 {Index} 次调用)",
                functionName,
                FormatArgumentNames(context),
                _authorizationMode,
                callIndex);

            bool authorized;
            try
            {
                authorized = await IsAuthorizedAsync(functionName, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "交易授权回调执行失败，已拒绝: {Function}", functionName);
                authorized = false;
            }

            if (!authorized)
            {
                _logger.LogWarning(
                    "交易操作未获授权，已拒绝: {Function}, AuthorizationMode: {AuthorizationMode}",
                    functionName,
                    _authorizationMode);
                context.Terminate = true;
                return $"操作未获授权，已拒绝: {functionName}";
            }
        }
        else
        {
            _logger.LogDebug("TradingAgent 工具调用: {Function} (第 {Index} 次)", functionName, callIndex);
        }

        // 3. 执行实际调用
        var result = await next(context, cancellationToken);

        // 4. 执行后审计
        if (IsSensitiveOperation(functionName))
        {
            _logger.LogInformation(
                "交易操作完成 [{Function}] 结果摘要: {ResultType}",
                functionName, result?.GetType().Name ?? "null");
        }

        return result;
    }

    internal IDisposable BeginRunBudgetScope()
    {
        var previous = _currentRunBudget.Value;
        _currentRunBudget.Value = new RunBudgetState();
        return new RunBudgetScope(_currentRunBudget, previous);
    }

    private async Task<bool> IsAuthorizedAsync(string functionName, string args)
    {
        return _authorizationMode switch
        {
            TradingAuthorizationMode.PreAuthorizedAutomation => true,
            TradingAuthorizationMode.InteractiveConfirmation when _confirmationCallback is not null
                => await _confirmationCallback(functionName, args),
            _ => false
        };
    }

    private static bool IsSensitiveOperation(string functionName)
    {
        return functionName is "PlaceOrderAsync" or "CancelOrderAsync";
    }

    private static string FormatArguments(FunctionInvocationContext context)
    {
        return context.Arguments != null
            ? JsonSerializer.Serialize(context.Arguments)
            : "N/A";
    }

    private static string FormatArgumentNames(FunctionInvocationContext context)
    {
        return context.Arguments is { Count: > 0 }
            ? string.Join(',', context.Arguments.Keys.OrderBy(name => name, StringComparer.Ordinal))
            : "N/A";
    }

    private sealed class RunBudgetState
    {
        public int ToolCallCount;
    }

    private sealed class RunBudgetScope(
        AsyncLocal<RunBudgetState?> currentRunBudget,
        RunBudgetState? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            currentRunBudget.Value = previous;
            _disposed = true;
        }
    }
}
