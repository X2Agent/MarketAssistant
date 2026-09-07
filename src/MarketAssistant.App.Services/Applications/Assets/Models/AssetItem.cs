namespace MarketAssistant.Applications.Assets.Models;

/// <summary>
/// 通用资产条目
/// </summary>
public class AssetItem
{
    /// <summary>
    /// 资产名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 资产代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格（仅用于首页最近查看等场景的展示，历史记录不依赖）
    /// </summary>
    public string CurrentPrice { get; set; } = string.Empty;

    /// <summary>
    /// 涨跌幅百分比（如 "+1.26%"，仅用于展示）
    /// </summary>
    public string ChangePercentage { get; set; } = string.Empty;

    public override string ToString() => string.IsNullOrEmpty(Name) ? Code : $"{Name} ({Code})";
}






