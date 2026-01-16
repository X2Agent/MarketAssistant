using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Applications.Favorites;

/// <summary>
/// 虚拟币收藏服务实现
/// </summary>
public class CryptoFavoriteService : IFavoriteService
{
    private const string PreferenceKey = "FavoriteAssets_Crypto";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CryptoFavoriteService> _logger;

    private IAssetInfoService AssetInfoService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketContext.CurrentMarket);
        }
    }

    public CryptoFavoriteService(
        IServiceProvider serviceProvider,
        ILogger<CryptoFavoriteService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// 添加虚拟币到收藏
    /// </summary>
    public void AddFavorite(string code, string market)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        var favoriteList = GetFavoritesCodes();

        var existingItem = favoriteList.FirstOrDefault(x => x.Code == code && x.Market == market);
        if (existingItem != null)
            return;

        favoriteList.Add(new FavoriteAsset { Code = code, Market = market });
        SaveFavorites(favoriteList);
        WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());

        _logger.LogInformation("已添加虚拟币到收藏: {Code}", code);
    }

    /// <summary>
    /// 从收藏中移除虚拟币
    /// </summary>
    public void RemoveFavorite(string code, string market)
    {
        var favoriteList = GetFavoritesCodes();

        var itemToRemove = favoriteList.FirstOrDefault(x => x.Code == code && x.Market == market);
        if (itemToRemove != null)
        {
            favoriteList.Remove(itemToRemove);
            SaveFavorites(favoriteList);
            WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());

            _logger.LogInformation("已从收藏中移除虚拟币: {Code}", code);
        }
    }

    /// <summary>
    /// 检查虚拟币是否已收藏
    /// </summary>
    public bool IsFavorite(string code, string market)
    {
        var favoriteList = GetFavoritesCodes();
        return favoriteList.Any(x => x.Code == code && x.Market == market);
    }

    /// <summary>
    /// 获取收藏的虚拟币代码列表
    /// </summary>
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
            _logger.LogError(ex, "获取收藏虚拟币时出错: {Message}", ex.Message);
            return new List<FavoriteAsset>();
        }
    }

    /// <summary>
    /// 获取收藏虚拟币的最新数据
    /// </summary>
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
                        _logger.LogError(ex, "获取虚拟币 {Code} 最新数据时出错: {Message}", favorite.Code, ex.Message);
                        return new AssetInfo
                        {
                            Code = favorite.Code,
                            Market = favorite.Market,
                            Name = ExtractBaseCurrency(favorite.Code),
                            MarketType = MarketType.Crypto
                        };
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
            _logger.LogError(ex, "获取收藏虚拟币最新数据时出错: {Message}", ex.Message);
            return assetInfos;
        }
    }

    /// <summary>
    /// 清空所有收藏
    /// </summary>
    public void ClearFavorites()
    {
        SaveFavorites(new List<FavoriteAsset>());
        WeakReferenceMessenger.Default.Send(new AssetFavoritesChanged());
        _logger.LogInformation("已清空所有收藏虚拟币");
    }

    /// <summary>
    /// 保存收藏列表到本地存储
    /// </summary>
    private void SaveFavorites(List<FavoriteAsset> favoriteList)
    {
        try
        {
            var json = JsonSerializer.Serialize(favoriteList);
            Preferences.Default.Set(PreferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存收藏虚拟币时出错: {Message}", ex.Message);
        }
    }
}






