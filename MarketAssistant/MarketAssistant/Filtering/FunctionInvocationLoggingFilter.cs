using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MarketAssistant.Filtering;

public sealed class FunctionInvocationLoggingFilter(ILogger<FunctionInvocationLoggingFilter> logger) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        long startingTimestamp = Stopwatch.GetTimestamp();

        logger.LogInformation("Function {FunctionName} invoking.", context.Function.Name);

        if (context.Arguments.Count > 0)
        {
            logger.LogTrace("Function arguments: {Arguments}", JsonSerializer.Serialize(context.Arguments));
        }

        if (logger.IsEnabled(LogLevel.Information) && context.Arguments.ExecutionSettings is not null)
        {
            logger.LogInformation("Execution settings: {Settings}", JsonSerializer.Serialize(context.Arguments.ExecutionSettings));
        }

        try
        {
            await next(context);

            logger.LogInformation("Function {FunctionName} succeeded.", context.Function.Name);

            if (context.IsStreaming)
            {
                // Overriding the result in a streaming scenario enables the filter to stream chunks 
                // back to the operation's origin without interrupting the data flow.
                var enumerable = context.Result.GetValue<IAsyncEnumerable<StreamingChatMessageContent>>();
                context.Result = new FunctionResult(context.Result, ProcessFunctionResultStreamingAsync(enumerable!));
            }
            else
            {
                ProcessFunctionResult(context.Result);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Function failed. Error: {Message}", exception.Message);
            throw;
        }
        finally
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                TimeSpan duration = new((long)((Stopwatch.GetTimestamp() - startingTimestamp) * (10_000_000.0 / Stopwatch.Frequency)));

                // Capturing the duration in seconds as per OpenTelemetry convention for instrument units:
                // More information here: https://opentelemetry.io/docs/specs/semconv/general/metrics/#instrument-units
                logger.LogInformation("Function completed. Duration: {Duration}s", duration.TotalSeconds);
            }
        }
    }

    private void ProcessFunctionResult(FunctionResult functionResult)
    {
        object? result = functionResult.GetValue<object>();

        if (result is string resultString)
        {
            logger.LogTrace("Function result: {Result}", resultString);
        }
        else
        {
            try
            {
                logger.LogTrace("Function result: {Result}", JsonSerializer.Serialize(result));
            }
            catch
            {
                logger.LogTrace("Function result: <Contains async enumerable - cannot serialize: {Type}>", result.GetType().Name);
            }
        }

        object? usage = functionResult.Metadata?["Usage"];

        if (logger.IsEnabled(LogLevel.Information) && usage is not null)
        {
            logger.LogInformation("Usage: {Usage}", JsonSerializer.Serialize(usage));
        }
    }

    private async IAsyncEnumerable<StreamingChatMessageContent> ProcessFunctionResultStreamingAsync(IAsyncEnumerable<StreamingChatMessageContent> data)
    {
        object? usage = null;

        var stringBuilder = new StringBuilder();

        await foreach (var item in data)
        {
            yield return item;

            if (item.Content is not null)
            {
                stringBuilder.Append(item.Content);
            }

            usage = item.Metadata?["Usage"];
        }

        var result = stringBuilder.ToString();

        if (!string.IsNullOrWhiteSpace(result))
        {
            logger.LogTrace("Function result: {Result}", result);
        }

        if (logger.IsEnabled(LogLevel.Information) && usage is not null)
        {
            logger.LogInformation("Usage: {Usage}", JsonSerializer.Serialize(usage));
        }
    }
}
