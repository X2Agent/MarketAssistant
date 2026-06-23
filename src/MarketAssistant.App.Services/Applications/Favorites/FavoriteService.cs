using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Applications.Favorites;

/// <summary>
/// 收藏服务：封装本地存储与最新行情查询的共同行为。
/// 通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型。
/// </summary>
public sealed class FavoriteService : IFavoriteService
{
    private readonly IAssetInfoService _assetInfoService;
    private readonly ILogger<FavoriteService> _logger;
    private readonly MarketType _marketType;
    private readonly string _preferenceKey;
    private readonly string _marketLabel;

    public FavoriteService(
        [ServiceKey] MarketType marketType,
        IServiceProvider serviceProvider,
        ILogger<FavoriteService> logger)
    {
        _marketType = marketType;
        _assetInfoService = serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketType);
        _logger = logger;
        _preferenceKey = PreferenceKeys.GetFavoriteAssetsKey(marketType);
        _marketLabel = marketType switch
        {
            MarketType.AShare => "A股",
            MarketType.Crypto => "虚拟币",
            _ => marketType.ToString()
        };
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
        _logger.LogInformation("已添加{Market}到收藏: {Code}", _marketLabel, normalizedFavorite.Code);
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
        _logger.LogInformation("已从收藏中移除{Market}: {Code}", _marketLabel, normalizedFavorite.Code);
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
            var json = Preferences.Default.Get(_preferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            var favoriteList = JsonSerializer.Deserialize<List<FavoriteAsset>>(json) ?? [];
            return favoriteList.Select(NormalizeFavorite).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取收藏{Market}时出错: {Message}", _marketLabel, ex.Message);
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
                return await _assetInfoService.GetAssetInfoAsync(favorite.Code, favorite.Market, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取{Market} {Code} 最新数据时出错: {Message}", _marketLabel, favorite.Code, ex.Message);
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
        _logger.LogInformation("已清空所有收藏{Market}", _marketLabel);
    }

    private static FavoriteAsset NormalizeFavorite(FavoriteAsset favorite)
    {
        return new FavoriteAsset
        {
            Code = favorite.Code.Trim(),
            Market = favorite.Market.Trim()
        };
    }

    /// <summary>
    /// 行情获取失败时的兜底 AssetInfo：A股使用 Market.Code 形式，虚拟币使用基础币种名称。
    /// </summary>
    private AssetInfo CreateFallbackAssetInfo(FavoriteAsset favorite)
    {
        var displayName = _marketType switch
        {
            MarketType.Crypto => ExtractBaseCurrency(favorite.Code),
            _ => string.IsNullOrWhiteSpace(favorite.Market)
                ? favorite.Code
                : $"{favorite.Market}.{favorite.Code}"
        };

        return new AssetInfo
        {
            Code = favorite.Code,
            Market = favorite.Market,
            Name = displayName,
            MarketType = _marketType
        };
    }

    private void SaveFavorites(List<FavoriteAsset> favoriteList)
    {
        try
        {
            var json = JsonSerializer.Serialize(favoriteList);
            Preferences.Default.Set(_preferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存收藏{Market}时出错: {Message}", _marketLabel, ex.Message);
        }
    }
}
