using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Technical;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Technical;
using MarketAssistant.Applications.Technical.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币技术分析工具实现（基于币安 K 线数据，指标计算复用 <see cref="ITechnicalIndicatorService"/>）
/// </summary>
public sealed class CryptoTechnicalTools(
    ILogger<CryptoTechnicalTools> logger,
    [FromKeyedServices(MarketType.Crypto)] IKLineService kLineService,
    [FromKeyedServices(MarketType.Crypto)] ITechnicalIndicatorService indicatorService) : ITechnicalDataTools
{
    /// <summary>指标计算所需的最大 K 线根数（MA250 需要 250 根）。</summary>
    private const int IndicatorBarCount = 250;

    private readonly ILogger<CryptoTechnicalTools> _logger = logger;
    private readonly IKLineService _kLineService = kLineService;
    private readonly ITechnicalIndicatorService _indicatorService = indicatorService;

    /// <summary>
    /// 四个指标方法共用的外壳：ToolExecutor 统一计时与异常转换、取日线指标快照；
    /// 指标专属的空值校验、模型映射与成功日志由 <paramref name="map"/> 完成。
    /// </summary>
    private Task<T> GetIndicatorAsync<T>(string indicator, string assetSymbol, Func<TechnicalIndicatorSnapshot, T> map, CancellationToken cancellationToken = default)
    {
        return ToolExecutor.ExecuteAsync($"计算虚拟币{indicator}指标", assetSymbol, _logger, async _ =>
        {
            var snapshot = await _indicatorService.GetSnapshotAsync(assetSymbol, KLineType.Daily, IndicatorBarCount, cancellationToken);
            return map(snapshot);
        }, cancellationToken);
    }

    [Description("获取近30日最新日线KDJ，支持BTC、ETH等币种")]
    public Task<TechnicalKDJ> GetKDJAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync("KDJ", assetSymbol, snapshot =>
        {
            if (snapshot.KdjK is null || snapshot.KdjD is null || snapshot.KdjJ is null)
                throw new FriendlyException($"K线数据不足，无法计算KDJ指标: {assetSymbol}");

            var kdj = new TechnicalKDJ
            {
                T = snapshot.Date,
                K = snapshot.KdjK,
                D = snapshot.KdjD,
                J = snapshot.KdjJ
            };

            _logger.LogInformation("成功计算虚拟币KDJ指标: {Symbol}, K={K}, D={D}, J={J}",
                assetSymbol, kdj.K, kdj.D, kdj.J);

            return kdj;
        }, cancellationToken);

    [Description("获取近30日最新日线MACD，支持BTC、ETH等币种")]
    public Task<TechnicalMACD> GetMACDAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync("MACD", assetSymbol, snapshot =>
        {
            if (snapshot.MacdDiff is null || snapshot.MacdDea is null)
                throw new FriendlyException($"K线数据不足，无法计算MACD指标: {assetSymbol}");

            var macd = new TechnicalMACD
            {
                T = snapshot.Date,
                Diff = snapshot.MacdDiff.Value,
                Dea = snapshot.MacdDea.Value,
                Macd = snapshot.MacdBar ?? 0,
                Ema12 = snapshot.MacdFastEma ?? 0,
                Ema26 = snapshot.MacdSlowEma ?? 0
            };

            _logger.LogInformation("成功计算虚拟币MACD指标: {Symbol}, DIFF={Diff}, DEA={Dea}, MACD={Macd}",
                assetSymbol, macd.Diff, macd.Dea, macd.Macd);

            return macd;
        }, cancellationToken);

    [Description("获取近30日最新日线BOLL，支持BTC、ETH等币种")]
    public Task<TechnicalBoll> GetBOLLAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync("BOLL", assetSymbol, snapshot =>
        {
            if (snapshot.BollUpper is null || snapshot.BollMiddle is null || snapshot.BollLower is null)
                throw new FriendlyException($"K线数据不足，无法计算BOLL指标: {assetSymbol}");

            var boll = new TechnicalBoll
            {
                T = snapshot.Date,
                U = snapshot.BollUpper,
                M = snapshot.BollMiddle,
                D = snapshot.BollLower
            };

            _logger.LogInformation("成功计算虚拟币BOLL指标: {Symbol}, 上轨={U}, 中轨={M}, 下轨={D}",
                assetSymbol, boll.U, boll.M, boll.D);

            return boll;
        }, cancellationToken);

    [Description("获取近30日最新日线MA，支持BTC、ETH等币种")]
    public Task<TechnicalMA> GetMAAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync("MA", assetSymbol, snapshot =>
        {
            if (!snapshot.HasAnyValue)
                throw new FriendlyException($"K线数据不足，无法计算MA指标: {assetSymbol}");

            var ma = new TechnicalMA
            {
                T = snapshot.Date,
                MA3 = snapshot.GetMovingAverage(3),
                MA5 = snapshot.GetMovingAverage(5),
                MA10 = snapshot.GetMovingAverage(10),
                MA15 = snapshot.GetMovingAverage(15),
                MA20 = snapshot.GetMovingAverage(20),
                MA30 = snapshot.GetMovingAverage(30),
                MA60 = snapshot.GetMovingAverage(60),
                MA120 = snapshot.GetMovingAverage(120),
                MA200 = snapshot.GetMovingAverage(200),
                MA250 = snapshot.GetMovingAverage(250)
            };

            _logger.LogInformation("成功计算虚拟币MA指标: {Symbol}, MA5={MA5}, MA10={MA10}, MA20={MA20}",
                assetSymbol, ma.MA5, ma.MA10, ma.MA20);

            return ma;
        }, cancellationToken);

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetKDJAsync);
        yield return AIFunctionFactory.Create(GetMACDAsync);
        yield return AIFunctionFactory.Create(GetBOLLAsync);
        yield return AIFunctionFactory.Create(GetMAAsync);
        yield return AIFunctionFactory.Create(GetKLinesAsync);
    }

    [Description("获取K线历史序列（OHLCV），interval支持5m/15m/daily/weekly，用于判断趋势方向及多周期确认")]
    public async Task<List<OhlcvBar>> GetKLinesAsync(
        [Description("虚拟币代码（如BTC、ETH）")] string assetSymbol,
        [Description("K线周期：5m/15m/daily/weekly")] string interval = "daily",
        [Description("返回根数，最大500")] int count = 30,
        CancellationToken cancellationToken = default)
    {
        return await ToolExecutor.ExecuteAsync<List<OhlcvBar>>("获取K线序列", assetSymbol, _logger, async _ =>
        {
            var klineType = interval.ToLowerInvariant() switch
            {
                "5m" => KLineType.Minute5,
                "15m" => KLineType.Minute15,
                "weekly" => KLineType.Weekly,
                _ => KLineType.Daily
            };

            var klineData = await _kLineService.GetKLineDataAsync(assetSymbol, klineType, Math.Clamp(count, 1, 500));

            if (klineData == null || klineData.Count == 0)
                throw new FriendlyException($"K线数据为空: {assetSymbol}");

            var timeFormat = klineType is KLineType.Minute5 or KLineType.Minute15
                ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd";

            return [.. klineData
                .OrderBy(k => k.Timestamp)
                .Select(k => new OhlcvBar
                {
                    T = k.Timestamp.ToString(timeFormat),
                    O = k.Open,
                    H = k.High,
                    L = k.Low,
                    C = k.Close,
                    V = k.Volume
                })];
        }, cancellationToken);
    }
}
