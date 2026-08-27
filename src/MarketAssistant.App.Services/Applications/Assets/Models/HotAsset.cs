using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.Assets.Models;

/// <summary>
/// 热门资产
/// </summary>
public class HotAsset
{
    /// <summary>
    /// 资产名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 涨跌幅
    /// </summary>
    public string ChangePercentage { get; set; } = string.Empty;

    /// <summary>
    /// 资产代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格
    /// </summary>
    public string CurrentPrice { get; set; } = string.Empty;

    /// <summary>
    /// 市场标识
    /// </summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 市场类型
    /// </summary>
    public MarketType MarketType { get; set; }

    /// <summary>
    /// 所属板块或分类（A股为板块名称，加密货币暂不使用）
    /// </summary>
    public string? SectorName { get; set; }

    /// <summary>
    /// 市值（仅加密货币使用）
    /// </summary>
    public string? MarketCap { get; set; }

    /// <summary>
    /// 核心指标值（股票为热度，加密货币为24小时交易量）
    /// </summary>
    public string? MetricValue { get; set; }

    /// <summary>
    /// 指标标签（根据市场类型动态返回）
    /// </summary>
    public string MetricLabel { get; set; } = "热度";

    /// <summary>
    /// 格式化的指标显示值
    /// </summary>
    public string FormattedMetric => FormatMetric(MetricValue);

    /// <summary>
    /// 行尾标签文本：A 股显示所属板块，加密货币显示核心指标（如"24h量 2.41B"）
    /// </summary>
    public string TagText => MarketType == MarketType.Crypto
        ? $"{MetricLabel} {FormattedMetric}"
        : SectorName ?? string.Empty;

    private static string FormatMetric(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        if (decimal.TryParse(value, out var volume))
        {
            if (volume >= 1_000_000_000)
                return $"{volume / 1_000_000_000:N2}B";
            if (volume >= 1_000_000)
                return $"{volume / 1_000_000:N2}M";
            if (volume >= 1_000)
                return $"{volume / 1_000:N2}K";

            return volume.ToString("N0");
        }

        return value;
    }
}






