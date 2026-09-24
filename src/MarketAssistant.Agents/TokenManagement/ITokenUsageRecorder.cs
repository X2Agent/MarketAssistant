namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// 运行级 Token 用量记录器。
/// </summary>
/// <remarks>
/// 存在的原因：分析工作流的 Agent 以 <c>session: null</c> 无状态方式调用，
/// <see cref="Middleware.TokenTrackingMiddleware"/> 的会话级累计（StateBag）不生效，
/// 导致分析路径的 Token 用量无处落地。记录器提供一条与会话无关的旁路通道：
/// 工作流开始时开启作用域，中间件把每次调用的用量上报到该作用域。
/// </remarks>
public interface ITokenUsageRecorder
{
    /// <summary>当前异步流程关联的作用域；不在任何运行内时为 <c>null</c>。</summary>
    ITokenUsageScope? CurrentScope { get; }

    /// <summary>
    /// 开始一次运行级作用域。作用域沿异步调用链向下传递，因此工作流内并发执行的
    /// 各 Agent 中间件会自动关联到该作用域。释放后恢复外层作用域。
    /// </summary>
    ITokenUsageScope BeginScope(Guid runId);
}

/// <summary>
/// 运行级 Token 用量作用域。实现必须线程安全：并发的 Agent 调用会同时上报。
/// </summary>
public interface ITokenUsageScope : IDisposable
{
    Guid RunId { get; }

    /// <summary>上报一次模型调用的用量。<paramref name="agentName"/> 为空时归入 "Unknown"。</summary>
    void Report(string? agentName, long inputTokens, long outputTokens, bool isPrecise);

    /// <summary>查询某个 Agent 的累计用量（输入、输出）；未记录时返回 <c>(0, 0)</c>。</summary>
    (long Input, long Output) TokensFor(string? agentName);

    /// <summary>生成当前累计用量的独立快照。</summary>
    AnalysisTokenUsage Snapshot();
}
