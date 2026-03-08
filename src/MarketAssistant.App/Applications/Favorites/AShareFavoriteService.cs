using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.Favorites;

/// <summary>
/// A股收藏服务实现
/// </summary>
public class AShareFavoriteService : IFavoriteService
{
    private const string PreferenceKey = "FavoriteAssets_AShare";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AShareFavoriteService> _logger;

    private IAssetInfoService AssetInfoService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketContext.CurrentMarket);
        }
    }

    public AShareFavoriteService(
        IServiceProvider serviceProvider,
        ILogger<AShareFavoriteService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void AddFavorite(string code, string market)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(market))
            return;

        var favoriteList = GetFavoritesCodes();

        var existingItem = favoriteList.FirstOrDefault(x => x.Code == code && x.Market == market);
        if (existingItem != null)
            return;

        favoriteList.Add(new FavoriteAsset { Code = code, Market = market });
        SaveFavorites(favoriteList);
        WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
    }

    public void RemoveFavorite(string code, string market)
    {
        var favoriteList = GetFavoritesCodes();

        var itemToRemove = favoriteList.FirstOrDefault(x => x.Code == code && x.Market == market);
        if (itemToRemove != null)
        {
            favoriteList.Remove(itemToRemove);
            SaveFavorites(favoriteList);
            WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
        }
    }

    public bool IsFavorite(string code, string market)
    {
        var favoriteList = GetFavoritesCodes();
        return favoriteList.Any(x => x.Code == code && x.Market == market);
    }

    public List<FavoriteAsset> GetFavoritesCodes()
    {
        try
        {
            var json = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return new List<FavoriteAsset>();

            var favoriteList = JsonSerializer.Deserialize<List<FavoriteAsset>>(json);
            return favoriteList ?? new List<FavoriteAsset>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取收藏资产时出错: {ex.Message}");
            return new List<FavoriteAsset>();
        }
    }

    public async Task<List<AssetInfo>> GetFavoritesWithLatestDataAsync(CancellationToken cancellationToken = default)
    {
        var favoritesCodes = GetFavoritesCodes();
        if (favoritesCodes.Count == 0)
            return new();

        var assetInfos = new List<AssetInfo>();

        try
        {
            var tasks = new List<Task<AssetInfo>>();

            foreach (var favorite in favoritesCodes)
            {
                var task = Task.Run(async () =>
                {
                    try
                    {
                        return await AssetInfoService.GetAssetInfoAsync(favorite.Code, favorite.Market, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"获取资产 {favorite.Code} 最新数据时出错: {ex.Message}");
                        return new AssetInfo { Code = favorite.Code, Market = favorite.Market, Name = $"{favorite.Market}.{favorite.Code}" };
                    }
                });

                tasks.Add(task);
            }

            var results = await Task.WhenAll(tasks);
            assetInfos.AddRange(results.Where(r => r != null));

            return assetInfos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取收藏资产最新数据时出错: {ex.Message}");
            return assetInfos;
        }
    }

    public void ClearFavorites()
    {
        SaveFavorites(new List<FavoriteAsset>());
        WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
    }

    private void SaveFavorites(List<FavoriteAsset> favoriteList)
    {
        try
        {
            var json = JsonSerializer.Serialize(favoriteList);
            Preferences.Default.Set(PreferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"保存收藏资产时出错: {ex.Message}");
        }
    }
}




