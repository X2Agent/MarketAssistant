using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.History;

namespace MarketAssistant.Applications.Home;

/// <summary>
/// 首页资产服务：封装搜索、热门与历史流程。
/// 通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型，
/// 构造时一次性解析同市场的 keyed 依赖。
/// </summary>
public sealed class HomeAssetService(
    [ServiceKey] MarketType marketType,
    IServiceProvider serviceProvider) : IHomeAssetService
{
    private readonly IAssetInfoService _assetInfoService = serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketType);
    private readonly IAssetHistoryService _historyService = serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(marketType);

    public async Task<List<AssetItem>> SearchAssetAsync(string query, CancellationToken cancellationToken = default)
    {
        // 空查询属于"无结果"，直接返回空列表；数据源故障由 SearchAsync 抛出，交由上层展示失败态
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var results = await _assetInfoService.SearchAsync(query, cancellationToken);
        return [.. results.Select(asset => new AssetItem { Name = asset.Name, Code = asset.Code })];
    }

    public Task<List<HotAsset>> GetHotAssetsAsync()
        => _assetInfoService.GetHotAssetsAsync();

    public Task<List<AssetItem>> GetRecentAssetsAsync(CancellationToken cancellationToken = default)
        => _historyService.GetHistoryAsync(cancellationToken);

    public async Task AddToRecentAssetsAsync(AssetItem asset, CancellationToken cancellationToken = default)
        => await _historyService.AddHistoryAsync(asset, cancellationToken);
}
