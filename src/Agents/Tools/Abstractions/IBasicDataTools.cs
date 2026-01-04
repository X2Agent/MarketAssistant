using MarketAssistant.Agents.Plugins.Models;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 基础数据工具接口
/// </summary>
public interface IBasicDataTools
{
    /// <summary>
    /// 根据资产代码获取基本数据，包括实时行情、价格变动、市值等信息
    /// </summary>
    Task<AssetQuoteInfo> GetAssetInfoAsync(string assetSymbol);

    /// <summary>
    /// 根据资产代码获取公司/项目基本面信息，包括简介、主营业务、所属行业等
    /// </summary>
    Task<CompanyInfo> GetCompanyInfoAsync(string assetSymbol);

    /// <summary>
    /// 获取AI工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}





