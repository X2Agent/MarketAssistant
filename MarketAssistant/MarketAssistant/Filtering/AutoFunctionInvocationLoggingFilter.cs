using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace MarketAssistant.Filtering;

/// <summary>
/// Filter which logs an information available during automatic function calling such as:
/// Chat history, number of functions to call, which functions to call and their arguments.
/// </summary>
public sealed class AutoFunctionInvocationLoggingFilter(ILogger<AutoFunctionInvocationLoggingFilter> logger) : IAutoFunctionInvocationFilter
{
    public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("ChatHistory: {ChatHistory}", JsonSerializer.Serialize(context.ChatHistory));
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Function count: {FunctionCount}", context.FunctionCount);
        }

        var functionCalls = FunctionCallContent.GetFunctionCalls(context.ChatHistory.Last()).ToList();

        if (logger.IsEnabled(LogLevel.Trace))
        {
            functionCalls.ForEach(functionCall
                => logger.LogTrace(
                    "Function call requests: {PluginName}-{FunctionName}({Arguments})",
                    functionCall.PluginName,
                    functionCall.FunctionName,
                    JsonSerializer.Serialize(functionCall.Arguments)));
        }

        await next(context);
    }
}
