using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Technical;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币技术分析工具实现（基于币安 K 线数据计算）
/// </summary>
public sealed class CryptoTechnicalTools : ITechnicalDataTools
{
    private readonly ILogger<CryptoTechnicalTools> _logger;
    private readonly IServiceProvider _serviceProvider;

    public CryptoTechnicalTools(ILogger<CryptoTechnicalTools> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    [Description("获取近30日最新日线KDJ，支持BTC、ETH等币种")]
    public async Task<TechnicalKDJ> GetKDJAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 9)
            {
                throw new FriendlyException($"K线数据不足，无法计算KDJ指标: {assetSymbol}");
            }

            var quotes = ToIndicatorQuotes(klineData);
            var result = quotes.GetStoch(9, 3, 3)
                .LastOrDefault(item => item.K is not null && item.D is not null && item.J is not null)
                ?? throw new FriendlyException($"KDJ 指标结果为空: {assetSymbol}");

            var kdj = new TechnicalKDJ
            {
                T = klineData.Last().Timestamp.ToString("yyyy-MM-dd"),
                K = Round(result.K),
                D = Round(result.D),
                J = Round(result.J)
            };

            _logger.LogInformation("成功计算虚拟币KDJ指标: {Symbol}, K={K}, D={D}, J={J}",
                assetSymbol, kdj.K, kdj.D, kdj.J);

            return kdj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币KDJ指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币KDJ指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    [Description("获取近30日最新日线MACD，支持BTC、ETH等币种")]
    public async Task<TechnicalMACD> GetMACDAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 26)
            {
                throw new FriendlyException($"K线数据不足，无法计算MACD指标: {assetSymbol}");
            }

            var quotes = ToIndicatorQuotes(klineData);
            var result = quotes.GetMacd(12, 26, 9)
                .LastOrDefault(item => item.Macd is not null && item.Signal is not null)
                ?? throw new FriendlyException($"MACD 指标结果为空: {assetSymbol}");

            var macd = new TechnicalMACD
            {
                T = klineData.Last().Timestamp.ToString("yyyy-MM-dd"),
                Diff = Round(result.Macd) ?? 0,
                Dea = Round(result.Signal) ?? 0,
                Macd = Round(result.Histogram) ?? 0,
                Ema12 = Round(result.FastEma) ?? 0,
                Ema26 = Round(result.SlowEma) ?? 0
            };

            _logger.LogInformation("成功计算虚拟币MACD指标: {Symbol}, DIFF={Diff}, DEA={Dea}, MACD={Macd}",
                assetSymbol, macd.Diff, macd.Dea, macd.Macd);

            return macd;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币MACD指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币MACD指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    [Description("获取近30日最新日线BOLL，支持BTC、ETH等币种")]
    public async Task<TechnicalBoll> GetBOLLAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 20)
            {
                throw new FriendlyException($"K线数据不足，无法计算BOLL指标: {assetSymbol}");
            }

            var quotes = ToIndicatorQuotes(klineData);
            var result = quotes.GetBollingerBands(20, 2)
                .LastOrDefault(item => item.UpperBand is not null && item.Sma is not null && item.LowerBand is not null)
                ?? throw new FriendlyException($"BOLL 指标结果为空: {assetSymbol}");

            var boll = new TechnicalBoll
            {
                T = klineData.Last().Timestamp.ToString("yyyy-MM-dd"),
                U = Round(result.UpperBand),
                M = Round(result.Sma),
                D = Round(result.LowerBand)
            };

            _logger.LogInformation("成功计算虚拟币BOLL指标: {Symbol}, 上轨={U}, 中轨={M}, 下轨={D}",
                assetSymbol, boll.U, boll.M, boll.D);

            return boll;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币BOLL指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币BOLL指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    [Description("获取近30日最新日线MA，支持BTC、ETH等币种")]
    public async Task<TechnicalMA> GetMAAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 3)
            {
                throw new FriendlyException($"K线数据不足，无法计算MA指标: {assetSymbol}");
            }

            var quotes = ToIndicatorQuotes(klineData);
            var ma = new TechnicalMA
            {
                T = klineData.Last().Timestamp.ToString("yyyy-MM-dd"),
                MA3 = GetLatestSma(quotes, 3),
                MA5 = GetLatestSma(quotes, 5),
                MA10 = GetLatestSma(quotes, 10),
                MA15 = GetLatestSma(quotes, 15),
                MA20 = GetLatestSma(quotes, 20),
                MA30 = GetLatestSma(quotes, 30),
                MA60 = GetLatestSma(quotes, 60),
                MA120 = GetLatestSma(quotes, 120),
                MA200 = GetLatestSma(quotes, 200),
                MA250 = GetLatestSma(quotes, 250)
            };

            _logger.LogInformation("成功计算虚拟币MA指标: {Symbol}, MA5={MA5}, MA10={MA10}, MA20={MA20}",
                assetSymbol, ma.MA5, ma.MA10, ma.MA20);

            return ma;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币MA指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币MA指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetKDJAsync);
        yield return AIFunctionFactory.Create(GetMACDAsync);
        yield return AIFunctionFactory.Create(GetBOLLAsync);
        yield return AIFunctionFactory.Create(GetMAAsync);
    }

    private static List<IndicatorQuote> ToIndicatorQuotes(IEnumerable<KLineData> klineData)
    {
        return klineData
            .OrderBy(item => item.Timestamp)
            .Select(item => new IndicatorQuote
            {
                Date = item.Timestamp,
                Open = item.Open,
                High = item.High,
                Low = item.Low,
                Close = item.Close,
                Volume = item.Volume
            })
            .ToList();
    }

    private static decimal? GetLatestSma(List<IndicatorQuote> quotes, int period)
    {
        return Round(quotes.GetSma(period).LastOrDefault(item => item.Sma is not null)?.Sma);
    }

    private static decimal? Round(double? value)
    {
        return value.HasValue ? Math.Round((decimal)value.Value, 2) : null;
    }

    private sealed class IndicatorQuote : IQuote
    {
        public DateTime Date { get; init; }
        public decimal Open { get; init; }
        public decimal High { get; init; }
        public decimal Low { get; init; }
        public decimal Close { get; init; }
        public decimal Volume { get; init; }
    }
}
