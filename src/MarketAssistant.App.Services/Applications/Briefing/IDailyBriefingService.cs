namespace MarketAssistant.Applications.Briefing;

/// <summary>
/// 每日 AI 简报服务（按市场 Keyed 注册）：基于自选行情与最新要闻生成
/// "隔夜要闻 + 异动归因"摘要，按本地日期缓存复用。
/// </summary>
public interface IDailyBriefingService
{
    /// <summary>读取当天已生成的简报；无缓存时返回 null。</summary>
    Task<DailyBriefing?> GetTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成当天简报。已有缓存且 <paramref name="force"/> 为 false 时直接返回缓存；
    /// 自选与要闻均为空时返回 null（无可摘要内容）；LLM 调用失败抛出异常，不落盘失败结果。
    /// </summary>
    /// <param name="force">跳过当天缓存强制重新生成（会再次产生模型调用与费用）。</param>
    /// <param name="manual">
    /// 是否为用户手动触发。自动路径（首页进入时）会受"自动生成"开关约束，
    /// 手动路径则只受"启用简报"总开关约束——用户显式点击时不应被二次拦截。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DailyBriefing?> GenerateAsync(
        bool force = false,
        bool manual = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否允许当前进行自动（用户未主动触发）生成。读取当天开关状态，
    /// 供 UI 决定是否展示"自动生成已关闭"提示。
    /// </summary>
    bool IsAutoGenerateEnabled { get; }

    /// <summary>简报功能总开关（关闭后手动生成亦不可用）。</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 汇总指定天数内（截至今天，含今天）本市场已生成简报的 Token 用量与调用次数。
    /// 用于回答"这个月简报一共花了多少"——单日消耗已可见，跨天累计此前不可见。
    /// </summary>
    /// <param name="days">回溯天数，取值 1 起；非法值按 1 处理。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DailyBriefingUsageSummary> GetUsageSummaryAsync(int days = 30, CancellationToken cancellationToken = default);
}

/// <summary>
/// 简报用量汇总（跨天）。
/// </summary>
/// <param name="DayCount">实际覆盖的天数（有生成记录的天数，可能小于请求的窗口）。</param>
/// <param name="InputTokens">窗口内输入 Token 合计。</param>
/// <param name="OutputTokens">窗口内输出 Token 合计。</param>
/// <param name="Estimated">是否包含本地估算值（提供商未返回精确用量）。</param>
public sealed record DailyBriefingUsageSummary(
    int DayCount,
    long InputTokens,
    long OutputTokens,
    bool Estimated)
{
    public bool HasValue => DayCount > 0;

    public long TotalTokens => InputTokens + OutputTokens;
}
