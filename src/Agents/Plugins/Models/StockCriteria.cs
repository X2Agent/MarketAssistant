using System.ComponentModel;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 股票筛选参数
/// </summary>
public class StockCriteria
{
    /// <summary>
    /// 筛选条件列表
    /// </summary>
    [Description("筛选条件")]
    public List<StockScreeningCriteria> Criteria { get; set; } = new();

    /// <summary>
    /// 市场类型：全部A股、沪市A股、深市A股等
    /// </summary>
    public string Market { get; set; } = "全部A股";

    /// <summary>
    /// 行业分类：全部、科技、金融等
    /// </summary>
    [Description("行业分类")]
    public string Industry { get; set; } = "全部";

    /// <summary>
    /// 返回数量限制
    /// </summary>
    public int Limit { get; set; } = 20;
}
