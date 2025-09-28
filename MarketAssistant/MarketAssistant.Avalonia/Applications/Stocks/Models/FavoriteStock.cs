namespace MarketAssistant.Applications.Stocks.Models;

public class FavoriteStock
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 市场代码
    /// </summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 获取完整股票代码（市场+代码）
    /// </summary>
    public string FullCode => $"{Market}{Code}".ToLower();
}