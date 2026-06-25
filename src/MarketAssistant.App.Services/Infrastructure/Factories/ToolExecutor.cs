using System.Diagnostics;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 工具执行器：统一封装 Agent Tool 的执行流程，包含日志记录、异常转换和计时。
/// </summary>
public static class ToolExecutor
{
    /// <summary>
    /// 执行工具操作并统一处理日志与异常。
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        string operationName,
        string assetSymbol,
        ILogger logger,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("开始{Operation}: {Symbol}", operationName, assetSymbol);

        try
        {
            var result = await action(cancellationToken);
            logger.LogInformation("完成{Operation}: {Symbol}, 耗时 {Elapsed}ms", operationName, assetSymbol, sw.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Operation}失败: {Symbol}", operationName, assetSymbol);
            throw new FriendlyException($"{operationName}失败: {ex.Message}", ex);
        }
    }
}
