namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// A股筛选结果（雪球 HTTP API 数据源）
/// </summary>
public class ScreenerStockInfo : ScreenerAssetInfo
{
    /// <summary>
    /// 所有数值字段（API 字段名 → 数值），包括 current/pct/mc 等。
    /// 字段名与雪球 screener API 返回的 JSON key 一致。
    /// API 只返回 order_by 和筛选条件涉及的字段，因此不同查询返回的字段不同。
    /// </summary>
    public Dictionary<string, decimal> Indicators { get; set; } = new();
}
