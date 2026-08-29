using MarketAssistant.Agents.Tools.Models.Crypto;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 虚拟币市场情绪工具接口
/// </summary>
public interface ICryptoSentimentTools : ISentimentTools
{
    /// <param name="symbol">交易对符号</param>
    /// <remarks>
    /// 数据源：币安 Futures API
    /// - 当前费率和下次结算时间：GET /fapi/v1/premiumIndex
    /// - 历史费率：GET /fapi/v1/fundingRate
    /// </remarks>
    Task<FundingRateHistory> GetFundingRateAsync(string symbol, CancellationToken cancellationToken = default);

    /// <param name="symbol">交易对符号</param>
    /// <param name="period">时间周期（5m/15m/30m/1h/2h/4h/6h/12h/1d）</param>
    /// <param name="limit">获取的数据点数量（默认30）</param>
    /// <remarks>
    /// 数据源：币安 Futures API - GET /futures/data/globalLongShortAccountRatio
    /// 含义：全市场所有账户的多空比，可与顶级交易员数据对比分析
    /// </remarks>
    Task<LongShortRatioHistory> GetGlobalLongShortRatioAsync(string symbol, Period period = Period.FiveMinutes, int limit = 30, CancellationToken cancellationToken = default);

    /// <param name="symbol">交易对符号</param>
    /// <param name="period">时间周期（5m/15m/30m/1h/2h/4h/6h/12h/1d）</param>
    /// <param name="limit">获取的数据点数量（默认30）</param>
    /// <remarks>
    /// 数据源：币安 Futures API - GET /futures/data/topLongShortAccountRatio
    /// 含义：大户账户数的多空比（按账户数量统计）
    /// </remarks>
    Task<LongShortRatioHistory> GetTopTraderAccountRatioAsync(string symbol, Period period = Period.FiveMinutes, int limit = 30, CancellationToken cancellationToken = default);

    /// <param name="symbol">交易对符号</param>
    /// <param name="period">时间周期（5m/15m/30m/1h/2h/4h/6h/12h/1d）</param>
    /// <param name="limit">获取的数据点数量（默认30）</param>
    /// <remarks>
    /// 数据源：币安 Futures API - GET /futures/data/topLongShortPositionRatio
    /// 含义：大户持仓量的多空比（按持仓金额统计），更能反映真实资金流向
    /// </remarks>
    Task<LongShortRatioHistory> GetTopTraderPositionRatioAsync(string symbol, Period period = Period.FiveMinutes, int limit = 30, CancellationToken cancellationToken = default);

    /// <param name="symbol">交易对符号</param>
    /// <param name="period">时间周期</param>
    /// <remarks>
    /// 数据源：币安 Futures API - GET /futures/data/openInterestHist
    /// </remarks>
    Task<OpenInterest> GetOpenInterestAsync(string symbol, Period period = Period.OneHour, CancellationToken cancellationToken = default);
}
