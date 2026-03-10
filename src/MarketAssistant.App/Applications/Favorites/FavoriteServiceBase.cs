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
/// 收藏服务基类，封装本地存储与最新行情查询的共同行为。
/// </summary>
public abstract class FavoriteServiceBase : IFavoriteService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    protected FavoriteServiceBase(IServiceProvider serviceProvider, ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected abstract string PreferenceKey { get; }

    protected IAssetInfoService AssetInfoService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketContext.CurrentMarket);
        }
    }

    public void AddFavorite(string code, string market)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var normalizedFavorite = NormalizeFavorite(new FavoriteAsset
        {
            Code = code,
            Market = market
        });

        var favoriteList = GetFavoritesCodes();
        var existingItem = favoriteList.FirstOrDefault(x => x.Code == normalizedFavorite.Code && x.Market == normalizedFavorite.Market);
        if (existingItem != null)
        {
            return;
        }

        favoriteList.Add(normalizedFavorite);
        SaveFavorites(favoriteList);
        WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
        LogFavoriteAdded(normalizedFavorite);
    }

    public void RemoveFavorite(string code, string market)
    {
        var normalizedFavorite = NormalizeFavorite(new FavoriteAsset
        {
            Code = code,
            Market = market
        });

        var favoriteList = GetFavoritesCodes();
        var itemToRemove = favoriteList.FirstOrDefault(x => x.Code == normalizedFavorite.Code && x.Market == normalizedFavorite.Market);

        if (itemToRemove == null)
        {
            return;
        }

        favoriteList.Remove(itemToRemove);
        SaveFavorites(favoriteList);
        WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
        LogFavoriteRemoved(normalizedFavorite);
    }

    public bool IsFavorite(string code, string market)
    {
        var normalizedFavorite = NormalizeFavorite(new FavoriteAsset
        {
            Code = code,
            Market = market
        });

        var favoriteList = GetFavoritesCodes();
        return favoriteList.Any(x => x.Code == normalizedFavorite.Code && x.Market == normalizedFavorite.Market);
    }

    public List<FavoriteAsset> GetFavoritesCodes()
    {
        try
        {
            var json = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            var favoriteList = JsonSerializer.Deserialize<List<FavoriteAsset>>(json) ?? [];
            return favoriteList.Select(NormalizeFavorite).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取收藏资产时出错: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<List<AssetInfo>> GetFavoritesWithLatestDataAsync(CancellationToken cancellationToken = default)
    {
        var favoritesCodes = GetFavoritesCodes();
        if (favoritesCodes.Count == 0)
        {
            return [];
        }

        var tasks = favoritesCodes.Select(async favorite =>
        {
            try
            {
                return await AssetInfoService.GetAssetInfoAsync(favorite.Code, favorite.Market, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取资产 {Code} 最新数据时出错: {Message}", favorite.Code, ex.Message);
                return CreateFallbackAssetInfo(favorite);
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(result => result != null).ToList();
    }

    public void ClearFavorites()
    {
        SaveFavorites([]);
        WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
        _logger.LogInformation("已清空所有收藏资产");
    }

    protected virtual FavoriteAsset NormalizeFavorite(FavoriteAsset favorite)
    {
        return new FavoriteAsset
        {
            Code = favorite.Code.Trim(),
            Market = favorite.Market.Trim()
        };
    }

    protected abstract AssetInfo CreateFallbackAssetInfo(FavoriteAsset favorite);

    protected virtual void LogFavoriteAdded(FavoriteAsset favorite)
    {
        _logger.LogInformation("已添加资产到收藏: {Code}", favorite.Code);
    }

    protected virtual void LogFavoriteRemoved(FavoriteAsset favorite)
    {
        _logger.LogInformation("已从收藏中移除资产: {Code}", favorite.Code);
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
            _logger.LogError(ex, "保存收藏资产时出错: {Message}", ex.Message);
        }
    }
}