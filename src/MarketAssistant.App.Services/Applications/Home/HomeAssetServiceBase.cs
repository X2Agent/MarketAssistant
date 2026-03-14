using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Home;

/// <summary>
/// 首页资产服务基类，封装通用的搜索、热门、历史与收藏流程。
/// </summary>
public abstract class HomeAssetServiceBase : IHomeAssetService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;
    private readonly ILogger _logger;

    protected HomeAssetServiceBase(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _logger = logger;
    }

    protected IAssetInfoService AssetInfoService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketContext.CurrentMarket);
        }
    }

    protected IAssetHistoryService HistoryService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(marketContext.CurrentMarket);
        }
    }

    protected IFavoriteService FavoriteService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IFavoriteService>(marketContext.CurrentMarket);
        }
    }

    public async Task<List<AssetItem>> SearchAssetAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var results = await AssetInfoService.SearchAsync(query, cancellationToken);
        return results.Select(asset => new AssetItem { Name = asset.Name, Code = asset.Code }).ToList();
    }

    public Task<List<HotAsset>> GetHotAssetsAsync()
    {
        return AssetInfoService.GetHotAssetsAsync();
    }

    public List<AssetItem> GetRecentAssets()
    {
        return HistoryService.GetHistory();
    }

    public void AddToRecentAssets(AssetItem asset)
    {
        HistoryService.AddHistory(asset);
    }

    public async Task<bool> AddToFavoriteAsync(object assetParameter)
    {
        if (!TryMapFavoriteAsset(assetParameter, out var favoriteRequest, out var errorMessage))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                _logger.LogWarning("添加到收藏失败: {Reason}", errorMessage);
                await _dialogService.ShowMessageAsync("错误", errorMessage);
            }

            return false;
        }

        if (FavoriteService.IsFavorite(favoriteRequest.Code, favoriteRequest.Market))
        {
            await _dialogService.ShowMessageAsync("提示", GetAlreadyFavoritedMessage(favoriteRequest));
            return false;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "添加收藏",
            $"确定要将 {favoriteRequest.AssetName} 添加到收藏列表吗？",
            "确认",
            "取消");

        if (!confirmed)
        {
            return false;
        }

        FavoriteService.AddFavorite(favoriteRequest.Code, favoriteRequest.Market);
        await _dialogService.ShowMessageAsync("收藏成功", $"已将 {favoriteRequest.AssetName} 添加到收藏列表");
        return true;
    }

    protected virtual string GetAlreadyFavoritedMessage(FavoriteAssetRequest favoriteRequest)
    {
        return "该资产已在收藏列表中";
    }

    protected abstract bool TryMapFavoriteAsset(
        object assetParameter,
        out FavoriteAssetRequest favoriteRequest,
        out string? errorMessage);

    protected sealed record FavoriteAssetRequest(string AssetName, string Code, string Market);
}