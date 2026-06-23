using MarketAssistant.Agents.Tools.Models.AShare;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 股票市场基础数据工具接口
/// </summary>
public interface IShareBasicTools : IBasicDataTools
{
    /// <summary>
    /// 根据股票代码获取基本数据，包括实时行情、价格变动、市值等信息
    /// </summary>
    Task<StockQuoteInfo> GetAssetInfoAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据股票代码获取上市公司基本面信息，包括简介、主营业务、所属行业等
    /// </summary>
    Task<CompanyInfo> GetCompanyInfoAsync(string assetSymbol, CancellationToken cancellationToken = default);
}
