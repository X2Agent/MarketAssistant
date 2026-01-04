using System.ComponentModel;
using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    [Description("获取近30日最新日线KDJ")]
    public async Task<TechnicalKDJ> GetKDJAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 30);

            if (klineData == null || klineData.Count < 9)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算KDJ指标: {assetSymbol}");
            }

            var kdj = CalculateKDJ(klineData);
            kdj.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");
            
            _logger.LogInformation("成功计算虚拟币KDJ指标: {Symbol}, K={K}, D={D}, J={J}", 
                assetSymbol, kdj.K, kdj.D, kdj.J);
            
            return kdj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币KDJ指标失败: {Symbol}", assetSymbol);
            throw;
        }
    }

    [Description("获取近30日最新日线MACD")]
    public async Task<TechnicalMACD> GetMACDAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 50);

            if (klineData == null || klineData.Count < 26)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算MACD指标: {assetSymbol}");
            }

            var macd = CalculateMACD(klineData);
            macd.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");
            
            _logger.LogInformation("成功计算虚拟币MACD指标: {Symbol}, DIFF={Diff}, DEA={Dea}, MACD={Macd}", 
                assetSymbol, macd.Diff, macd.Dea, macd.Macd);
            
            return macd;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币MACD指标失败: {Symbol}", assetSymbol);
            throw;
        }
    }

    [Description("获取近30日最新日线BOLL")]
    public async Task<TechnicalBoll> GetBOLLAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 30);

            if (klineData == null || klineData.Count < 20)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算BOLL指标: {assetSymbol}");
            }

            var boll = CalculateBOLL(klineData);
            boll.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");
            
            _logger.LogInformation("成功计算虚拟币BOLL指标: {Symbol}, 上轨={U}, 中轨={M}, 下轨={D}", 
                assetSymbol, boll.U, boll.M, boll.D);
            
            return boll;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币BOLL指标失败: {Symbol}", assetSymbol);
            throw;
        }
    }

    [Description("获取近30日最新日线MA")]
    public async Task<TechnicalMA> GetMAAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 3)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算MA指标: {assetSymbol}");
            }

            var ma = CalculateMA(klineData);
            ma.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");
            
            _logger.LogInformation("成功计算虚拟币MA指标: {Symbol}, MA5={MA5}, MA10={MA10}, MA20={MA20}", 
                assetSymbol, ma.MA5, ma.MA10, ma.MA20);
            
            return ma;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币MA指标失败: {Symbol}", assetSymbol);
            throw;
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetKDJAsync);
        yield return AIFunctionFactory.Create(GetMACDAsync);
        yield return AIFunctionFactory.Create(GetBOLLAsync);
        yield return AIFunctionFactory.Create(GetMAAsync);
    }

    #region 技术指标计算方法

    /// <summary>
    /// 计算KDJ指标（RSV -> K -> D -> J）
    /// </summary>
    private TechnicalKDJ CalculateKDJ(List<KLineData> klineData, int period = 9)
    {
        var rsvList = new List<decimal>();
        var kList = new List<decimal>();
        var dList = new List<decimal>();

        for (int i = 0; i < klineData.Count; i++)
        {
            if (i < period - 1)
            {
                rsvList.Add(50); // 前N天RSV默认50
                continue;
            }

            var recentData = klineData.Skip(i - period + 1).Take(period).ToList();
            var low = recentData.Min(x => x.Low);
            var high = recentData.Max(x => x.High);
            var close = klineData[i].Close;

            var rsv = high == low ? 50 : (close - low) / (high - low) * 100;
            rsvList.Add(rsv);
        }

        // 计算K值（K = 2/3 * 前一日K + 1/3 * RSV）
        decimal prevK = 50;
        for (int i = 0; i < rsvList.Count; i++)
        {
            var k = (2m / 3m) * prevK + (1m / 3m) * rsvList[i];
            kList.Add(k);
            prevK = k;
        }

        // 计算D值（D = 2/3 * 前一日D + 1/3 * K）
        decimal prevD = 50;
        for (int i = 0; i < kList.Count; i++)
        {
            var d = (2m / 3m) * prevD + (1m / 3m) * kList[i];
            dList.Add(d);
            prevD = d;
        }

        // 计算J值（J = 3K - 2D）
        var lastK = kList.Last();
        var lastD = dList.Last();
        var lastJ = 3 * lastK - 2 * lastD;

        return new TechnicalKDJ
        {
            K = Math.Round(lastK, 2),
            D = Math.Round(lastD, 2),
            J = Math.Round(lastJ, 2)
        };
    }

    /// <summary>
    /// 计算MACD指标（EMA12, EMA26, DIFF, DEA, MACD）
    /// </summary>
    private TechnicalMACD CalculateMACD(List<KLineData> klineData)
    {
        var closePrices = klineData.Select(x => x.Close).ToList();

        // 计算EMA12和EMA26
        var ema12 = CalculateEMA(closePrices, 12);
        var ema26 = CalculateEMA(closePrices, 26);

        // 计算DIFF (EMA12 - EMA26)
        var diffList = new List<decimal>();
        for (int i = 0; i < ema12.Count; i++)
        {
            diffList.Add(ema12[i] - ema26[i]);
        }

        // 计算DEA (DIFF的9日EMA)
        var dea = CalculateEMA(diffList, 9);

        // 计算MACD柱 (DIFF - DEA) * 2
        var lastDiff = diffList.Last();
        var lastDea = dea.Last();
        var macdBar = (lastDiff - lastDea) * 2;

        return new TechnicalMACD
        {
            Ema12 = Math.Round(ema12.Last(), 2),
            Ema26 = Math.Round(ema26.Last(), 2),
            Diff = Math.Round(lastDiff, 2),
            Dea = Math.Round(lastDea, 2),
            Macd = Math.Round(macdBar, 2)
        };
    }

    /// <summary>
    /// 计算布林带指标（上轨、中轨、下轨）
    /// </summary>
    private TechnicalBoll CalculateBOLL(List<KLineData> klineData, int period = 20, decimal multiplier = 2)
    {
        var closePrices = klineData.TakeLast(period).Select(x => x.Close).ToList();

        // 计算中轨（MA20）
        var middle = closePrices.Average();

        // 计算标准差
        var variance = closePrices.Sum(x => (x - middle) * (x - middle)) / period;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        // 计算上下轨
        var upper = middle + multiplier * stdDev;
        var lower = middle - multiplier * stdDev;

        return new TechnicalBoll
        {
            U = Math.Round(upper, 2),
            M = Math.Round(middle, 2),
            D = Math.Round(lower, 2)
        };
    }

    /// <summary>
    /// 计算多周期MA指标
    /// </summary>
    private TechnicalMA CalculateMA(List<KLineData> klineData)
    {
        var closePrices = klineData.Select(x => x.Close).ToList();

        return new TechnicalMA
        {
            MA3 = CalculateSMA(closePrices, 3),
            MA5 = CalculateSMA(closePrices, 5),
            MA10 = CalculateSMA(closePrices, 10),
            MA15 = CalculateSMA(closePrices, 15),
            MA20 = CalculateSMA(closePrices, 20),
            MA30 = CalculateSMA(closePrices, 30),
            MA60 = CalculateSMA(closePrices, 60),
            MA120 = CalculateSMA(closePrices, 120),
            MA200 = CalculateSMA(closePrices, 200),
            MA250 = CalculateSMA(closePrices, 250)
        };
    }

    /// <summary>
    /// 计算简单移动平均线（SMA）
    /// </summary>
    private decimal? CalculateSMA(List<decimal> prices, int period)
    {
        if (prices.Count < period) return null;
        return Math.Round(prices.TakeLast(period).Average(), 2);
    }

    /// <summary>
    /// 计算指数移动平均线（EMA）
    /// </summary>
    private List<decimal> CalculateEMA(List<decimal> prices, int period)
    {
        var emaList = new List<decimal>();
        var multiplier = 2m / (period + 1);

        // 第一个EMA使用SMA
        var sma = prices.Take(period).Average();
        emaList.Add(sma);

        // 后续EMA计算
        for (int i = period; i < prices.Count; i++)
        {
            var ema = (prices[i] - emaList.Last()) * multiplier + emaList.Last();
            emaList.Add(ema);
        }

        return emaList;
    }

    #endregion
}
