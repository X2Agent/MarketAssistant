namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// <see cref="ITokenUsageRecorder"/> 的默认实现。
/// </summary>
/// <remarks>
/// 作用域通过 <see cref="AsyncLocal{T}"/> 沿异步调用链向下传递（与
/// <see cref="System.Diagnostics.Activity.Current"/> 同一机制），
/// 因此 MAF 工作流内并发执行的各 Agent 中间件无需显式注入即可关联到发起运行的作用域。
/// 单例注册即可：每个异步流程各自持有自己的值。
/// </remarks>
public sealed class TokenUsageRecorder : ITokenUsageRecorder
{
    /// <summary>Agent 名缺失时的归集键。</summary>
    public const string UnknownAgentName = "Unknown";

    private readonly AsyncLocal<ITokenUsageScope?> _current = new();

    public ITokenUsageScope? CurrentScope => _current.Value;

    public ITokenUsageScope BeginScope(Guid runId)
    {
        var scope = new TokenUsageScope(_current, runId, _current.Value);
        _current.Value = scope;
        return scope;
    }

    private sealed class TokenUsageScope(
        AsyncLocal<ITokenUsageScope?> slot,
        Guid runId,
        ITokenUsageScope? parent) : ITokenUsageScope
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, TokenUsageEntry> _entries = new(StringComparer.Ordinal);

        private long _inputTokens;
        private long _outputTokens;
        private int _callCount;
        private bool _isEstimated;
        private bool _disposed;

        public Guid RunId { get; } = runId;

        public void Report(string? agentName, long inputTokens, long outputTokens, bool isPrecise)
        {
            if (inputTokens <= 0 && outputTokens <= 0)
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(agentName) ? UnknownAgentName : agentName;

            // 故意不使用 checked：这里位于 Agent 调用链路内，
            // 溢出（实际不可能达到 long 上限）不应把异常抛进分析流程。
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _inputTokens += inputTokens;
                _outputTokens += outputTokens;
                _callCount++;
                if (!isPrecise)
                {
                    _isEstimated = true;
                }

                if (!_entries.TryGetValue(name, out var entry))
                {
                    entry = new TokenUsageEntry { AgentName = name };
                    _entries[name] = entry;
                }

                entry.InputTokens += inputTokens;
                entry.OutputTokens += outputTokens;
                entry.CallCount++;
                if (!isPrecise)
                {
                    entry.IsEstimated = true;
                }
            }
        }

        public (long Input, long Output) TokensFor(string? agentName)
        {
            if (string.IsNullOrEmpty(agentName))
            {
                return (0, 0);
            }

            lock (_gate)
            {
                return _entries.TryGetValue(agentName, out var entry)
                    ? (entry.InputTokens, entry.OutputTokens)
                    : (0, 0);
            }
        }

        public AnalysisTokenUsage Snapshot()
        {
            lock (_gate)
            {
                var entries = new List<TokenUsageEntry>(_entries.Count);
                foreach (var entry in _entries.Values)
                {
                    entries.Add(new TokenUsageEntry
                    {
                        AgentName = entry.AgentName,
                        InputTokens = entry.InputTokens,
                        OutputTokens = entry.OutputTokens,
                        CallCount = entry.CallCount,
                        IsEstimated = entry.IsEstimated
                    });
                }

                return new AnalysisTokenUsage
                {
                    InputTokens = _inputTokens,
                    OutputTokens = _outputTokens,
                    CallCount = _callCount,
                    IsEstimated = _isEstimated,
                    Entries = entries
                };
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            slot.Value = parent;
        }
    }
}
