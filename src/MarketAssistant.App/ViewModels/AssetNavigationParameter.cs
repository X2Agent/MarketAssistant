namespace MarketAssistant.ViewModels;

/// <summary>
/// 资产导航参数
/// </summary>
public class AssetNavigationParameter
{
    public string Code { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// 当前价格（可选）- 用于立即显示基本信息
    /// </summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>
    /// 涨跌幅（可选）- 用于立即显示基本信息
    /// </summary>
    public decimal? ChangePercent { get; set; }

    public AssetNavigationParameter(string code, string name, decimal? currentPrice = null, decimal? changePercent = null)
    {
        Code = code;
        Name = name;
        CurrentPrice = currentPrice;
        ChangePercent = changePercent;
    }
}






