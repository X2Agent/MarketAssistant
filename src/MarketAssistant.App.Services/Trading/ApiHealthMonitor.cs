namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易所 API 健康度监控：滑动窗口内的失败计数，供风控做 API 异常熔断。
/// </summary>
/// <remarks>
/// 为什么需要它：账户余额/持仓查询失败时风控已经 fail-closed 拒单，
/// 但"偶发失败"和"API 持续不可用"是两种状态。后者意味着所有依赖交易所数据的判断
/// （仓位、敞口、强平价）都不可信，此时应显式熔断并告警，而不是让每笔订单
/// 都以"无法获取账户信息"的理由单独失败。
/// </remarks>
public sealed class ApiHealthMonitor
{
    /// <summary>滑动窗口长度。窗口外的失败记录自动过期。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly object _sync = new();
    private readonly Queue<DateTime> _failures = [];
    private int _consecutiveFailures;

    /// <summary>当前窗口内的失败次数。</summary>
    public int FailureCount
    {
        get
        {
            lock (_sync)
            {
                PruneLocked(DateTime.UtcNow);
                return _failures.Count;
            }
        }
    }

    /// <summary>连续失败次数（成功后归零，用于日志与告警文案）。</summary>
    public int ConsecutiveFailures
    {
        get
        {
            lock (_sync)
            {
                return _consecutiveFailures;
            }
        }
    }

    /// <summary>窗口内失败次数是否达到熔断阈值。</summary>
    public bool IsUnhealthy(int threshold)
        => threshold > 0 && FailureCount >= threshold;

    /// <summary>记录一次成功（重置连续失败计数）。</summary>
    public void RecordSuccess()
    {
        lock (_sync)
        {
            _consecutiveFailures = 0;
        }
    }

    /// <summary>记录一次失败。</summary>
    public void RecordFailure()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            PruneLocked(now);
            _failures.Enqueue(now);
            _consecutiveFailures++;
        }
    }

    /// <summary>清空全部记录（环境切换等场景调用）。</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _failures.Clear();
            _consecutiveFailures = 0;
        }
    }

    private void PruneLocked(DateTime now)
    {
        while (_failures.Count > 0 && now - _failures.Peek() > Window)
            _failures.Dequeue();
    }
}
