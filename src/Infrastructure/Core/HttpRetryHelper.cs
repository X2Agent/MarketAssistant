using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 轻量级 HTTP 请求重试工具（桌面应用场景，仅处理瞬时故障）
/// </summary>
public static class HttpRetryHelper
{
    /// <summary>
    /// 带重试的 HTTP 操作执行器
    /// 仅对瞬时失败（网络异常、超时、5xx）重试，其他异常直接抛出
    /// </summary>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        int maxRetries = 1,
        int baseDelayMs = 1000,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                var delay = baseDelayMs * (attempt + 1);
                logger.LogWarning(ex, "HTTP 请求瞬时失败（第{Attempt}次），{Delay}ms 后重试", attempt + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 判断异常是否为瞬时故障（值得重试）
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        if (ex is TaskCanceledException { InnerException: TimeoutException })
            return true;

        if (ex is HttpRequestException httpEx)
        {
            var statusCode = (int?)httpEx.StatusCode;
            return statusCode is null or (>= 500 and <= 599) or 429;
        }

        return false;
    }
}
