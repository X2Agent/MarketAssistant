using MarketAssistant.Applications.Assets.Models;

namespace MarketAssistant.Applications.History;

/// <summary>
/// 资产历史记录服务接口
/// </summary>
public interface IAssetHistoryService
{
    /// <summary>
    /// 添加一条资产访问记录
    /// </summary>
    Task AddHistoryAsync(AssetItem asset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取历史记录列表
    /// </summary>
    Task<List<AssetItem>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空历史记录
    /// </summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
}
