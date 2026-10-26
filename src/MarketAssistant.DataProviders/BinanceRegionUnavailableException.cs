namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安因地区限制拒绝服务（HTTP 451 / 响应含 restricted location）。
/// </summary>
/// <remarks>
/// 单独建型而非复用 <see cref="Infrastructure.Core.FriendlyException"/>：
/// 该场景<b>重试无意义</b>——地区限制不会随时间恢复，只有用户配置代理才能改变。
/// 调用方（尤其是 WebSocket 重连循环）据此<b>停止重试</b>，避免无限重连刷屏
/// （实测 451 下每 5 秒重试一次，日志被完全淹没并持续占用网络与日志 IO）。
/// </remarks>
public sealed class BinanceRegionUnavailableException : Infrastructure.Core.FriendlyException
{
    public BinanceRegionUnavailableException(string message)
        : base(message)
    {
    }

    public BinanceRegionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
