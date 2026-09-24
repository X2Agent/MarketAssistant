namespace MarketAssistant.Applications.Market.Models;

/// <summary>
/// 行情速览快照（市场无关）。字段为 null 表示数据源未提供该指标。
/// </summary>
public sealed record MarketSnapshot(
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? PreClose,
    decimal? Volume,
    decimal? Amount,
    decimal? TurnoverRate = null,
    decimal? Amplitude = null,
    decimal? VolumeRatio = null,
    decimal? Pe = null,
    decimal? Pb = null)
{
    /// <summary>是否存在任何可用字段；全为 null 视为无数据。</summary>
    public bool HasAnyValue =>
        Open.HasValue || High.HasValue || Low.HasValue || PreClose.HasValue ||
        Volume.HasValue || Amount.HasValue || TurnoverRate.HasValue ||
        Amplitude.HasValue || VolumeRatio.HasValue || Pe.HasValue || Pb.HasValue;
}
