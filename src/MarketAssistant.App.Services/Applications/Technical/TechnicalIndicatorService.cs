using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Technical.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;

namespace MarketAssistant.Applications.Technical;

/// <summary>
/// 基于 Skender.Stock.Indicators 的指标实现，输入为市场无关的 <see cref="KLineData"/>，
/// 因此 A 股与虚拟币共用同一份计算逻辑。
/// </summary>
public sealed class TechnicalIndicatorService(
    IKLineService kLineService,
    ILogger<TechnicalIndicatorService> logger) : ITechnicalIndicatorService
{
    /// <summary>MACD 慢线周期，也是所有指标中最长的预热窗口。</summary>
    private const int MinimumBarCount = 26;

    /// <summary>RSI 回溯周期（Wilder 标准 14）。</summary>
    private const int RsiPeriod = 14;

    private readonly IKLineService _kLineService = kLineService ?? throw new ArgumentNullException(nameof(kLineService));
    private readonly ILogger<TechnicalIndicatorService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<TechnicalIndicatorSnapshot> GetSnapshotAsync(
        string code,
        KLineType kLineType = KLineType.Daily,
        int barCount = 250,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new FriendlyException("代码不能为空，无法计算技术指标");

        var kLineData = await _kLineService.GetKLineDataAsync(code, kLineType, Math.Max(barCount, MinimumBarCount));
        if (kLineData is null || kLineData.Count == 0)
            throw new FriendlyException($"K线数据为空，无法计算技术指标: {code}");

        var snapshot = CalculateFrom(kLineData);

        _logger.LogInformation(
            "计算技术指标完成: {Code} {Interval}, MA5={MA5}, DIFF={Diff}, K={K}, RSI={Rsi}",
            code, kLineType, snapshot.GetMovingAverage(5), snapshot.MacdDiff, snapshot.KdjK, snapshot.Rsi);

        return snapshot;
    }

    /// <inheritdoc />
    public TechnicalIndicatorSnapshot CalculateFrom(IReadOnlyList<KLineData> kLineData)
    {
        ArgumentNullException.ThrowIfNull(kLineData);
        if (kLineData.Count == 0)
            throw new FriendlyException("K线数据为空，无法计算技术指标");

        var quotes = ToIndicatorQuotes(kLineData);
        return BuildSnapshot(quotes, kLineData.OrderBy(item => item.Timestamp).Last().Timestamp);
    }

    private static TechnicalIndicatorSnapshot BuildSnapshot(List<IndicatorQuote> quotes, DateTime timestamp)
    {
        var movingAverages = TechnicalIndicatorSnapshot.MovingAveragePeriods
            .ToDictionary(period => period, period => GetLatestSma(quotes, period));

        var macd = quotes.GetMacd(12, 26, 9)
            .LastOrDefault(item => item.Macd is not null && item.Signal is not null);
        var diff = Round(macd?.Macd);
        var dea = Round(macd?.Signal);

        var boll = quotes.GetBollingerBands(20, 2)
            .LastOrDefault(item => item.UpperBand is not null && item.Sma is not null && item.LowerBand is not null);

        var stoch = quotes.GetStoch(9, 3, 3)
            .LastOrDefault(item => item.K is not null && item.D is not null && item.J is not null);

        return new TechnicalIndicatorSnapshot(
            Timestamp: timestamp,
            MovingAverages: movingAverages,
            MacdDiff: diff,
            MacdDea: dea,
            // 国内惯例 MACD 柱线 = 2*(DIFF-DEA)，与模型契约及 A 股智兔数据口径保持一致
            MacdBar: diff.HasValue && dea.HasValue ? 2 * (diff.Value - dea.Value) : null,
            MacdFastEma: Round(macd?.FastEma),
            MacdSlowEma: Round(macd?.SlowEma),
            BollUpper: Round(boll?.UpperBand),
            BollMiddle: Round(boll?.Sma),
            BollLower: Round(boll?.LowerBand),
            KdjK: Round(stoch?.K),
            KdjD: Round(stoch?.D),
            KdjJ: Round(stoch?.J),
            Rsi: GetLatestRsi(quotes));
    }

    /// <summary>最新 RSI 值（样本不足时返回 null）。</summary>
    private static decimal? GetLatestRsi(List<IndicatorQuote> quotes) =>
        Round(quotes.GetRsi(RsiPeriod).LastOrDefault(item => item.Rsi is not null)?.Rsi);

    private static List<IndicatorQuote> ToIndicatorQuotes(IEnumerable<KLineData> kLineData) =>
        [.. kLineData
            .OrderBy(item => item.Timestamp)
            .Select(item => new IndicatorQuote
            {
                Date = item.Timestamp,
                Open = item.Open,
                High = item.High,
                Low = item.Low,
                Close = item.Close,
                Volume = item.Volume
            })];

    private static decimal? GetLatestSma(List<IndicatorQuote> quotes, int period) =>
        Round(quotes.GetSma(period).LastOrDefault(item => item.Sma is not null)?.Sma);

    private static decimal? Round(double? value) =>
        value.HasValue ? Math.Round((decimal)value.Value, 2) : null;

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
