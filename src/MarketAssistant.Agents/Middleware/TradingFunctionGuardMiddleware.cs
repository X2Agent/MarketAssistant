using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Middleware;

/// <summary>
/// 交易工具调用守卫中间件，拦截 Agent 的函数调用实现：
/// 1. 敏感操作（PlaceOrder）审计日志
/// 2. 单次 Agent 运行内的工具调用计数限制
/// 3. 需人工确认时可终止调用链
/// </summary>
public sealed class TradingFunctionGuardMiddleware
{
    /// <summary>
    /// 需人工确认的回调。返回 true 表示用户确认放行，false 表示拒绝。
    /// 未设置时自动拒绝需确认的操作。
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmationCallback { get; set; }

    private readonly ILogger _logger;
    private readonly int _maxToolCalls;

    private int _toolCallCount;

    /// <param name="maxToolCalls">单次 Agent 运行最大工具调用次数，防止无限循环（默认 20）</param>
    public TradingFunctionGuardMiddleware(ILogger<TradingFunctionGuardMiddleware> logger, int maxToolCalls = 20)
    {
        _logger = logger;
        _maxToolCalls = maxToolCalls;
    }

    /// <summary>
    /// Function Calling 中间件入口，通过 agent.AsBuilder().Use(this.InvokeAsync).Build() 附加
    /// </summary>
    public async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var functionName = context.Function.Name;
        var callIndex = Interlocked.Increment(ref _toolCallCount);

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
                "交易敏感操作拦截 [{Function}] 参数: {Args} (第 {Index} 次调用)",
                functionName, args, callIndex);

            if (ConfirmationCallback != null)
            {
                var approved = await ConfirmationCallback(functionName, args);
                if (!approved)
                {
                    _logger.LogInformation("用户拒绝交易操作: {Function}", functionName);
                    return $"操作已被用户取消: {functionName}";
                }
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

    /// <summary>
    /// 重置调用计数（每次新的 Agent Run 前调用）
    /// </summary>
    public void ResetCallCount() => Interlocked.Exchange(ref _toolCallCount, 0);

    private static bool IsSensitiveOperation(string functionName)
    {
        return functionName is "PlaceOrderAsync" or "CancelOrderAsync";
    }

    private static string FormatArguments(FunctionInvocationContext context)
    {
        try
        {
            var args = context.Function.JsonSchema;
            return args.ToString() ?? "N/A";
        }
        catch
        {
            return "N/A";
        }
    }
}
