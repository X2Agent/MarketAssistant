using MarketAssistant.Agents.Tools.Models.Crypto;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 虚拟币市场基础数据工具接口
/// </summary>
public interface ICryptoBasicTools : IBasicDataTools
{
    /// <summary>
    /// 根据虚拟币代码获取基本数据，包括实时行情、价格变动、成交量等信息
    /// </summary>
    Task<CryptoQuoteInfo> GetAssetInfoAsync(string assetSymbol);

    /// <summary>
    /// 根据虚拟币代码获取区块链项目基本面信息，包括项目简介、社区数据、开发者活跃度等
    /// </summary>
    Task<CryptoProjectInfo> GetProjectInfoAsync(string assetSymbol);
}
