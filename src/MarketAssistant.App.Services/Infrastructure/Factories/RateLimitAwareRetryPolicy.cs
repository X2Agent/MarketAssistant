using System.ClientModel.Primitives;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 在 SDK 默认重试之上区分两类 429：
/// 免费模型日配额耗尽（free-models-per-day，UTC 零点才恢复）重试只会白等，直接放行；
/// 带 X-RateLimit-Reset 的分钟级限流等到窗口重置再重试；其余情况沿用 SDK 默认退避（含 Retry-After）。
/// 仅覆盖异步路径（应用内 LLM 调用均为异步）。
/// </summary>
internal sealed class RateLimitAwareRetryPolicy(int maxRetries, ILogger? logger) : ClientRetryPolicy(maxRetries)
{
    private const string DailyQuotaMarker = "free-models-per-day";
    private const string ResetHeaderName = "X-RateLimit-Reset";
    private static readonly TimeSpan ResetMargin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxResetWait = TimeSpan.FromSeconds(90);

    protected override async ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception)
    {
        if (message.Response is { Status: 429 } response)
        {
            // 未缓冲时读取 Content 会抛异常；缓冲后 SDK 构造 ClientResultException 仍能读到正文
            await response.BufferContentAsync().ConfigureAwait(false);
            if (response.Content.ToString().Contains(DailyQuotaMarker, StringComparison.Ordinal))
                return false;
        }

        return await base.ShouldRetryAsync(message, exception).ConfigureAwait(false);
    }

    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        var defaultDelay = base.GetNextDelay(message, tryCount);
        if (message.Response is not { Status: 429 } response
            || !response.Headers.TryGetValue(ResetHeaderName, out var resetValue)
            || !long.TryParse(resetValue, NumberStyles.None, CultureInfo.InvariantCulture, out var resetMilliseconds))
        {
            return defaultDelay;
        }

        // 重置时间已过、短于默认退避或远于上限（非分钟级窗口）时都沿用默认，避免长时间静默等待
        var waitUntilReset = DateTimeOffset.FromUnixTimeMilliseconds(resetMilliseconds) - DateTimeOffset.UtcNow + ResetMargin;
        if (waitUntilReset <= defaultDelay || waitUntilReset > MaxResetWait)
            return defaultDelay;

        logger?.LogWarning(
            "模型服务限流（429），等待 {WaitSeconds:F1} 秒至窗口重置后重试（第 {TryCount} 次）",
            waitUntilReset.TotalSeconds,
            tryCount);
        return waitUntilReset;
    }
}
