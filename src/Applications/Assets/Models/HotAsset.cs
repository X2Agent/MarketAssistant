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
    /// 排名变化
    /// </summary>
    public string RankChange { get; set; } = string.Empty;

    /// <summary>
    /// 综合热度
    /// </summary>
    public string HeatIndex { get; set; } = string.Empty;

    // A股特有字段
    /// <summary>
    /// 所属板块名称（A股特有）
    /// </summary>
    public string? SectorName { get; set; }

    // 虚拟币特有字段
    /// <summary>
    /// 市值（虚拟币特有）
    /// </summary>
    public string? MarketCap { get; set; }

    /// <summary>
    /// 24小时交易量（虚拟币特有）
    /// </summary>
    public string? Volume24h { get; set; }

    /// <summary>
    /// 指标标签（根据市场类型动态返回"热度"或"交易量"）
    /// </summary>
    public string MetricLabel => MarketType == MarketType.Crypto ? "交易量" : "热度";

    /// <summary>
    /// 指标数值（根据市场类型动态返回格式化的热度或交易量）
    /// </summary>
    public string MetricValue
    {
        get
        {
            if (MarketType == MarketType.Crypto)
            {
                return FormatVolume(Volume24h);
            }
            return FormatNumber(HeatIndex);
        }
    }

    private static string FormatNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        if (decimal.TryParse(value, out var number))
        {
            return number.ToString("N0");
        }

        return value;
    }

    private static string FormatVolume(string? value)
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






