using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易紧急停止状态（单例，进程内内存态 + SQLite 持久化）。
/// </summary>
/// <remarks>
/// <para>停机是资金安全的总闸：停机期间 <see cref="RiskManager"/> 拒绝所有新订单、
/// <see cref="MarketMonitor"/> 拒绝启动策略监控，且状态跨重启生效——
/// 否则应用重启后持久化为 Active 的策略会自动复活，紧急停止失去意义。</para>
/// <para>内存态经 <see cref="EnsureLoadedAsync"/> 单飞加载，风控热路径只读 volatile 字段；
/// 切换交易模式不清除停机（切环境不等于解除紧急停止）。</para>
/// </remarks>
public sealed class TradingHaltState(
    TradingDataService dataService,
    ILogger<TradingHaltState> logger)
{
    private readonly object _loadSync = new();
    private Task? _loadTask;
    private int _stateVersion;
    private volatile bool _loaded;
    private volatile bool _loadFailed;
    private volatile bool _halted;
    private volatile string _reason = string.Empty;

    /// <summary>停机状态变化（登记/解除）时触发，UI 据此刷新横幅。</summary>
    public event Action<bool>? HaltStateChanged;

    /// <summary>当前是否处于紧急停止状态。</summary>
    public bool IsHalted => _halted;

    /// <summary>持久层加载是否处于失败态（fail-closed 期间为 true，下次 EnsureLoadedAsync 将重试）。</summary>
    public bool HasLoadError => _loadFailed;

    /// <summary>停机原因（未停机时为空）。</summary>
    public string HaltReason => _reason;

    /// <summary>
    /// 确保已从持久化加载停机状态（成功加载后只读内存；若上次读取失败则重试）。
    /// 风控与监控启动路径在判定前调用，读取失败时按 fail-closed 置为停机，防止数据库故障放行交易。
    /// </summary>
    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        lock (_loadSync)
        {
            if (_loaded)
                return Task.CompletedTask;

            if (_loadTask is { IsCompleted: false })
                return _loadTask;

            var expectedVersion = _stateVersion;
            _loadTask = LoadCoreAsync(expectedVersion, ct);
            return _loadTask;
        }
    }

    private async Task LoadCoreAsync(int expectedVersion, CancellationToken ct)
    {
        try
        {
            var (halted, reason, _) = await dataService.LoadTradingHaltAsync(ct).ConfigureAwait(false);
            lock (_loadSync)
            {
                if (_loaded || _stateVersion != expectedVersion)
                    return;

                _halted = halted;
                _reason = halted ? (reason ?? string.Empty) : string.Empty;
                _loadFailed = false;
                _loaded = true;
            }
        }
        catch (Exception ex)
        {
            // fail-closed：无法确认数据库中的紧急停止标记时必须阻止新交易与监控启动，
            // 同时保持 _loaded = false 以便后续调用继续重试读取
            lock (_loadSync)
            {
                if (_loaded || _stateVersion != expectedVersion)
                    return;

                _halted = true;
                _reason = "无法读取紧急停止状态（数据库异常），已进入安全保护";
                _loadFailed = true;
            }
            logger.LogError(ex, "加载交易紧急停止状态失败，按 fail-closed 进入停机保护状态");
        }
    }

    /// <summary>登记紧急停止（内存 + 持久化）。</summary>
    public async Task HaltAsync(string reason, CancellationToken ct = default)
    {
        await dataService.SaveTradingHaltAsync(true, reason, ct).ConfigureAwait(false);
        lock (_loadSync)
        {
            _stateVersion++;
            _halted = true;
            _reason = reason;
            _loadFailed = false;
            _loaded = true;
        }
        logger.LogWarning("交易已紧急停止: {Reason}", reason);
        HaltStateChanged?.Invoke(true);
    }

    /// <summary>解除紧急停止（内存 + 持久化）。策略保持暂停，由用户逐个重新启用。</summary>
    public async Task ResumeAsync(CancellationToken ct = default)
    {
        await dataService.SaveTradingHaltAsync(false, string.Empty, ct).ConfigureAwait(false);
        lock (_loadSync)
        {
            _stateVersion++;
            _halted = false;
            _reason = string.Empty;
            _loadFailed = false;
            _loaded = true;
        }
        logger.LogInformation("交易紧急停止已解除");
        HaltStateChanged?.Invoke(false);
    }
}
