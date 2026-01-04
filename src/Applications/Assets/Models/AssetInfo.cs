using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.Assets.Models;

/// <summary>
/// 通用资产详情信息
/// </summary>
public class AssetInfo
{
    /// <summary>
    /// 资产代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 资产名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 市场类型
    /// </summary>
    public MarketType MarketType { get; set; }

    /// <summary>
    /// 当前价格
    /// </summary>
    public string CurrentPrice { get; set; } = string.Empty;

    /// <summary>
    /// 涨跌幅百分比
    /// </summary>
    public string ChangePercentage { get; set; } = string.Empty;

    /// <summary>
    /// 市场标识（如SH、SZ、BTC/USDT等）
    /// </summary>
    public string Market { get; set; } = string.Empty;

    // A股特有字段
    /// <summary>
    /// 所属板块名称（A股特有）
    /// </summary>
    public string? SectorName { get; set; }

    // 虚拟币特有字段
    /// <summary>
    /// 区块链网络（虚拟币特有）
    /// </summary>
    public string? BlockchainNetwork { get; set; }

    /// <summary>
    /// 市值（虚拟币特有）
    /// </summary>
    public string? MarketCap { get; set; }

    /// <summary>
    /// 24小时交易量（虚拟币特有）
    /// </summary>
    public string? Volume24h { get; set; }
}






