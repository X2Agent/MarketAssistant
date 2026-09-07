namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易拒绝原因分类：风控拒绝与用户主动拒绝（含人工确认超时）分别由不同告警源负责，
/// 避免同一事件产生重复告警。判定依据为 <see cref="TradeExecutor"/> 写入的 ErrorMessage 前缀。
/// </summary>
public static class TradeRejectionReason
{
    /// <summary>用户主动拒绝或确认超时的错误信息前缀。</summary>
    public const string UserRejectionPrefix = "用户拒绝";

    /// <summary>是否为用户主动拒绝类失败（此类不属风控拦截，改由策略状态信号告警覆盖）。</summary>
    public static bool IsUserRejection(string? errorMessage) =>
        errorMessage?.StartsWith(UserRejectionPrefix, StringComparison.Ordinal) == true;
}
