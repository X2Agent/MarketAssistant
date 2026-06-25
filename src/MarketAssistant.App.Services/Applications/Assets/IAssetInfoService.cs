using MarketAssistant.Applications.Assets.Models;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 资产信息服务接口
/// </summary>
public interface IAssetInfoService
{
    /// <summary>
    /// 搜索资产
    /// </summary>
    Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取资产详情
    /// </summary>
    Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取热门资产
    /// </summary>
    Task<List<HotAsset>> GetHotAssetsAsync();
}






