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
    Task<DailyBriefing?> GenerateAsync(bool force = false, CancellationToken cancellationToken = default);
}
