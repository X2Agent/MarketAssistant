using MarketAssistant.Agents.TokenManagement;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// <see cref="MarketAnalysisWorkflow"/> 的运行状态跟踪部分：负责把 MAF ExecutorId 解析为
/// 分析师显示名，并维护每个分析师的执行状态与对外快照。
/// </summary>
/// <remarks>
/// 从主文件拆出的原因：这些成员构成一个内聚的子职责（进度跟踪），
/// 与工作流编排逻辑无关，混在一起会让主文件难以通读。
/// 使用 <c>partial</c> 而非独立类，是为了让嵌套类型 <c>AnalystRunState</c> 与
/// <see cref="AnalystRunSnapshot"/> 的可见性、命名空间与外部引用面完全不变——
/// 已有多个调用方与测试直接引用它们。
/// </remarks>
public partial class MarketAnalysisWorkflow
{
    /// <summary>
    /// 将所有运行状态与指定显示名匹配的分析师标记为失败（降级隔离回调场景）。
    /// </summary>
    private static void MarkAnalystFailed(
        Dictionary<string, AnalystRunState> analystRunStates,
        string displayName)
    {
        foreach (var state in analystRunStates.Values)
        {
            if (string.Equals(state.DisplayName, displayName, StringComparison.Ordinal))
            {
                state.MarkFailed();
            }
        }
    }

    /// <summary>
    /// 生成各分析师的运行状态快照。运行中的条目携带实时耗时，已完成/失败的条目携带终态耗时。
    /// </summary>
    /// <param name="analystRunStates">键为 MAF Agent ASCII Name 的运行状态表。</param>
    /// <param name="tokenUsageScope">运行级 Token 用量作用域，用于回填各分析师的 Token 数；可为 null。</param>
    private static List<AnalystRunSnapshot> BuildAnalystSnapshots(
        Dictionary<string, AnalystRunState> analystRunStates,
        ITokenUsageScope? tokenUsageScope = null)
    {
        var snapshots = new List<AnalystRunSnapshot>(analystRunStates.Count);
        foreach (var (agentName, state) in analystRunStates)
        {
            var (inputTokens, outputTokens) = tokenUsageScope?.TokensFor(agentName) ?? (0, 0);
            snapshots.Add(state.CreateSnapshot(inputTokens, outputTokens));
        }
        return snapshots;
    }

    /// <summary>
    /// 单个分析师的运行状态（Run 局部，非线程安全：仅工作流事件循环与降级回调写入）。
    /// </summary>
    private sealed class AnalystRunState(string displayName)
    {
        public string DisplayName { get; } = displayName;

        public AnalystRunStatus Status { get; private set; } = AnalystRunStatus.Pending;

        /// <summary>真实执行（TurnToken）阶段开始的时间戳，完成/失败后清空。</summary>
        private long? StartedAtTimestamp { get; set; }

        /// <summary>终态耗时（完成或失败时定格）。</summary>
        private TimeSpan LastElapsed { get; set; }

        public void MarkRunning()
        {
            Status = AnalystRunStatus.Running;
            StartedAtTimestamp = Stopwatch.GetTimestamp();
        }

        public void MarkSucceeded(TimeSpan elapsed)
        {
            Status = AnalystRunStatus.Completed;
            StartedAtTimestamp = null;
            LastElapsed = elapsed;
        }

        public void MarkFailed()
        {
            if (Status == AnalystRunStatus.Running && StartedAtTimestamp.HasValue)
            {
                LastElapsed = Stopwatch.GetElapsedTime(StartedAtTimestamp.Value);
            }
            Status = AnalystRunStatus.Failed;
            StartedAtTimestamp = null;
        }

        /// <summary>
        /// 运行中被取消时定格耗时并按完成收尾（UI 无"已取消"状态，取消视为正常中断）。
        /// </summary>
        public void MarkSucceededAsStale()
        {
            if (Status == AnalystRunStatus.Running && StartedAtTimestamp.HasValue)
            {
                LastElapsed = Stopwatch.GetElapsedTime(StartedAtTimestamp.Value);
                Status = AnalystRunStatus.Completed;
            }
            StartedAtTimestamp = null;
        }

        public AnalystRunSnapshot CreateSnapshot(long inputTokens = 0, long outputTokens = 0)
        {
            var elapsed = Status == AnalystRunStatus.Running && StartedAtTimestamp.HasValue
                ? Stopwatch.GetElapsedTime(StartedAtTimestamp.Value)
                : LastElapsed;
            return new AnalystRunSnapshot(DisplayName, Status, elapsed, inputTokens, outputTokens);
        }
    }

    /// <summary>
    /// 根据分析师类型获取显示名称（用于创建失败时的降级提示）。
    /// 优先读取 <see cref="DisplayNameAttribute"/>，回退到类型名。
    /// </summary>
    private static string GetAnalystDisplayNameFromType(Type analystType)
    {
        return analystType.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
            ?? analystType.Name;
    }

    /// <summary>
    /// 从 ExecutorId 中提取 Name 前缀（第一个下划线之前的部分）。
    /// <para>
    /// MAF 的 <c>AIAgentExtensions.GetDescriptiveId</c> 生成 ExecutorId 的方式为
    /// <c>Regex.Replace(Name + "_" + Id, "[^0-9A-Za-z]+", "_")</c>。由于 <c>Name</c>
    /// 为 ASCII 标识符、<c>Id</c> 为 GUID 的 "N" 格式（纯十六进制），二者均不含下划线，
    /// 因此 ExecutorId 中有且仅有一个下划线作为分隔符。
    /// </para>
    /// </summary>
    private static string GetExecutorNamePrefix(string executorId)
    {
        var separatorIndex = executorId.IndexOf('_');
        return separatorIndex > 0 ? executorId[..separatorIndex] : executorId;
    }

    /// <summary>
    /// 判断是否为系统 Executor（Dispatcher / Aggregator / Coordinator），
    /// 即非分析师的业务执行器。
    /// </summary>
    private static bool IsSystemExecutor(string executorId)
    {
        var prefix = GetExecutorNamePrefix(executorId);
        return prefix is "AnalysisDispatcher" or "AnalysisAggregator" or "Coordinator";
    }

    /// <summary>
    /// 判断是否为分析师 Executor（排除系统 Executor）。
    /// </summary>
    private static bool IsAnalystExecutor(string executorId) => !IsSystemExecutor(executorId);

    /// <summary>
    /// 从工作流 ExecutorId 中提取分析师显示名称。
    /// 按第一个下划线切出 Name 前缀，再在 <see cref="_agentNameToDisplayName"/> 中查中文显示名。
    /// </summary>
    private static string GetDisplayNameForExecutorId(
        string executorId,
        IReadOnlyDictionary<string, string> agentNameToDisplayName)
    {
        var namePrefix = GetExecutorNamePrefix(executorId);

        return agentNameToDisplayName.TryGetValue(namePrefix, out var displayName)
            ? displayName
            : executorId;
    }

    /// <summary>
    /// 构建工作流错误的用户可读消息
    /// </summary>
    private static string BuildWorkflowErrorMessage(
        string baseMessage,
        List<(string DisplayName, string ErrorMessage)> failedSteps)
    {
        if (failedSteps.Count == 0)
        {
            return $"分析过程发生内部错误: {baseMessage}";
        }

        var failedDetails = string.Join("；",
            failedSteps.Select(f => $"「{f.DisplayName}」({f.ErrorMessage})"));
        return $"分析过程中以下环节出现问题: {failedDetails}。错误详情: {baseMessage}";
    }

    /// <summary>
    /// 构建工作流未输出报告时的用户可读消息
    /// </summary>
    private static string BuildOutputMissingErrorMessage(
        string assetSymbol,
        List<(string DisplayName, string ErrorMessage)> failedSteps,
        int completedAnalysts,
        int totalAnalysts)
    {
        if (failedSteps.Count > 0)
        {
            var failedDetails = string.Join("；",
                failedSteps.Select(f => $"「{f.DisplayName}」({f.ErrorMessage})"));
            return $"分析 {assetSymbol} 时部分环节失败导致无法生成报告: {failedDetails}";
        }

        if (completedAnalysts < totalAnalysts)
        {
            return $"分析 {assetSymbol} 未能完成: 仅 {completedAnalysts}/{totalAnalysts} 位分析师完成了分析，报告生成被中断";
        }

        return $"分析 {assetSymbol} 的所有分析师已完成，但综合报告生成环节异常，请重试。如果问题持续，请检查 AI 模型配置是否正确";
    }
}
