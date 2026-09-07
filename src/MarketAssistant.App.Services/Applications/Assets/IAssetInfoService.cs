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

    /// <summary>
    /// 批量获取资产详情（收藏列表等场景）。返回列表与 <paramref name="favorites"/> 等长同序，失败项为 null。
    /// 默认实现为受限并发逐个调用 <see cref="GetAssetInfoAsync"/>；
    /// 拥有批量行情源的市场（如虚拟币）应覆写为单次批量请求以降低首屏延迟。
    /// </summary>
    async Task<List<AssetInfo?>> GetAssetInfosAsync(
        IReadOnlyList<FavoriteAsset> favorites, CancellationToken cancellationToken = default)
    {
        var results = new AssetInfo?[favorites.Count];
        const int maxConcurrency = 3;
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = favorites.Select(async (favorite, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                results[index] = await GetAssetInfoAsync(favorite.Code, favorite.Market, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 单项失败不影响其余项，调用方以 null 占位跳过
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }
}






