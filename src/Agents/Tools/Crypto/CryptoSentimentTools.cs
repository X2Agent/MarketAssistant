using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币市场情绪工具实现
/// https://developers.binance.com/docs/zh-CN/derivatives/usds-margined-futures/general-info
/// </summary>
public sealed class CryptoSentimentTools : ICryptoSentimentTools
{
    private readonly ILogger<CryptoSentimentTools> _logger;
    private readonly BinanceMarketDataService _binanceService;

    public CryptoSentimentTools(
        ILogger<CryptoSentimentTools> logger,
        BinanceMarketDataService binanceService)
    {
        _logger = logger;
        _binanceService = binanceService;
    }

    /// <summary>
    /// 获取资金费率历史数据
    /// </summary>
    public async Task<FundingRateHistory> GetFundingRateAsync(string symbol)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);

            // 1. 获取当前资金费率
            var premiumResponse = await _binanceService.GetPremiumIndexAsync(binanceSymbol);

            if (premiumResponse == null)
            {
                throw new FriendlyException($"获取当前资金费率失败: {symbol}");
            }

            // 2. 获取历史资金费率
            var historyResponse = await _binanceService.GetFundingRateHistoryAsync(binanceSymbol, 30);

            if (historyResponse == null || historyResponse.Count == 0)
            {
                throw new FriendlyException($"获取历史资金费率失败: {symbol}");
            }

            // 3. 构建历史数据点（倒序排列，最新在前）
            var historyPoints = historyResponse
                .OrderByDescending(h => h.FundingTime)
                .Select(h => new FundingRatePoint
                {
                    Rate = decimal.Parse(h.FundingRate) * 100, // 转换为百分比
                    FundingTime = h.FundingTime
                })
                .ToList();

            if (historyPoints.Count == 0)
            {
                throw new FriendlyException($"解析历史资金费率数据为空: {symbol}");
            }

            // 4. 计算统计数据
            var currentRate = decimal.Parse(premiumResponse.LastFundingRate) * 100;
            var currentTime = historyPoints[0].FundingTime;
            var averageRate = historyPoints.Average(p => p.Rate);

            return new FundingRateHistory
            {
                Symbol = premiumResponse.Symbol,
                CurrentRate = currentRate,
                CurrentFundingTime = currentTime,
                NextFundingTime = premiumResponse.NextFundingTime,
                AverageRate = averageRate,
                History = historyPoints
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取资金费率历史数据时发生错误: {Symbol}", symbol);
            throw;
        }
    }

    /// <summary>
    /// 获取全局账户多空比历史数据
    /// </summary>
    public async Task<LongShortRatioHistory> GetGlobalLongShortRatioAsync(string symbol, Period period = Period.FiveMinutes, int limit = 30)
    {
        return await GetLongShortRatioHistoryAsync(
            symbol,
            period,
            limit,
            "globalLongShortAccountRatio",
            "全局账户多空比"
        );
    }

    /// <summary>
    /// 获取顶级交易员账户多空比历史数据
    /// </summary>
    public async Task<LongShortRatioHistory> GetTopTraderAccountRatioAsync(string symbol, Period period = Period.FiveMinutes, int limit = 30)
    {
        return await GetLongShortRatioHistoryAsync(
            symbol,
            period,
            limit,
            "topLongShortAccountRatio",
            "顶级交易员账户多空比"
        );
    }

    /// <summary>
    /// 获取顶级交易员持仓多空比历史数据
    /// </summary>
    public async Task<LongShortRatioHistory> GetTopTraderPositionRatioAsync(string symbol, Period period = Period.FiveMinutes, int limit = 30)
    {
        return await GetLongShortRatioHistoryAsync(
            symbol,
            period,
            limit,
            "topLongShortPositionRatio",
            "顶级交易员持仓多空比"
        );
    }

    /// <summary>
    /// 通用多空比历史数据获取方法
    /// </summary>
    private async Task<LongShortRatioHistory> GetLongShortRatioHistoryAsync(
        string symbol,
        Period period,
        int limit,
        string endpoint,
        string dataType)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);

            // 转换枚举为 API 参数
            var periodParam = period.GetDescription();

            // 获取历史数据
            var response = await _binanceService.GetLongShortRatioAsync(endpoint, binanceSymbol, periodParam, limit);

            if (response == null || response.Count == 0)
            {
                throw new FriendlyException($"获取{dataType}失败: {symbol}");
            }

            // 按时间倒序排列（最新在前）
            var sortedData = response.OrderByDescending(r => r.Timestamp).ToList();

            // 构建历史数据点
            var historyPoints = sortedData
                .Select(h => new LongShortRatioPoint
                {
                    LongRatio = decimal.Parse(h.LongAccount),
                    ShortRatio = decimal.Parse(h.ShortAccount),
                    Ratio = decimal.Parse(h.LongShortRatio),
                    Timestamp = h.Timestamp
                })
                .ToList();

            if (historyPoints.Count == 0)
            {
                throw new FriendlyException($"解析{dataType}数据为空: {symbol}");
            }

            // 计算统计数据
            var current = historyPoints[0];
            var averageRatio = historyPoints.Average(p => p.Ratio);

            return new LongShortRatioHistory
            {
                Symbol = binanceSymbol,
                CurrentLongRatio = current.LongRatio,
                CurrentShortRatio = current.ShortRatio,
                CurrentRatio = current.Ratio,
                AverageRatio = averageRatio,
                History = historyPoints
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取{DataType}时发生错误: {Symbol}", dataType, symbol);
            throw;
        }
    }

    /// <summary>
    /// 获取合约持仓量
    /// </summary>
    public async Task<OpenInterest> GetOpenInterestAsync(string symbol, Period period = Period.OneHour)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);

            // 转换枚举为 API 参数
            var periodParam = period.GetDescription();

            // 获取合约持仓量历史数据（默认获取最近30个数据点）
            var response = await _binanceService.GetOpenInterestHistAsync(binanceSymbol, periodParam, 30);

            if (response == null || response.Count == 0)
            {
                throw new FriendlyException($"获取合约持仓量失败: {symbol}");
            }

            // 按时间倒序排列（最新在前）
            var sortedData = response.OrderByDescending(r => r.Timestamp).ToList();

            // 构建历史数据点
            var historyPoints = sortedData
                .Select(h => new OpenInterestPoint
                {
                    SumOpenInterest = decimal.Parse(h.SumOpenInterest),
                    SumOpenInterestValue = decimal.Parse(h.SumOpenInterestValue),
                    Timestamp = h.Timestamp
                })
                .ToList();

            if (historyPoints.Count == 0)
            {
                throw new FriendlyException($"解析合约持仓量数据为空: {symbol}");
            }

            // 计算统计数据
            var current = historyPoints[0];
            var avgOpenInterest = historyPoints.Average(p => p.SumOpenInterest);
            var avgOpenInterestValue = historyPoints.Average(p => p.SumOpenInterestValue);

            return new OpenInterest
            {
                Symbol = binanceSymbol,
                CurrentOpenInterest = current.SumOpenInterest,
                CurrentOpenInterestValue = current.SumOpenInterestValue,
                CurrentTimestamp = current.Timestamp,
                AverageOpenInterest = avgOpenInterest,
                AverageOpenInterestValue = avgOpenInterestValue,
                History = historyPoints
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取合约持仓量时发生错误: {Symbol}", symbol);
            throw;
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetFundingRateAsync);
        yield return AIFunctionFactory.Create(GetGlobalLongShortRatioAsync);
        yield return AIFunctionFactory.Create(GetTopTraderAccountRatioAsync);
        yield return AIFunctionFactory.Create(GetTopTraderPositionRatioAsync);
        yield return AIFunctionFactory.Create(GetOpenInterestAsync);
    }
}
