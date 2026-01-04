using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Home;

/// <summary>
/// 虚拟币首页服务实现
/// </summary>
public class CryptoHomeService : IHomeAssetService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;
    private readonly ILogger<CryptoHomeService> _logger;

    private IAssetInfoService AssetInfoService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketContext.CurrentMarket);
        }
    }

    private IAssetHistoryService HistoryService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(marketContext.CurrentMarket);
        }
    }

    private IFavoriteService FavoriteService
    {
        get
        {
            var marketContext = _serviceProvider.GetRequiredService<MarketContext>();
            return _serviceProvider.GetRequiredKeyedService<IFavoriteService>(marketContext.CurrentMarket);
        }
    }

    public CryptoHomeService(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILogger<CryptoHomeService> logger)
    {
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _logger = logger;
    }

    /// <summary>
    /// 搜索虚拟币
    /// </summary>
    public async Task<List<AssetItem>> SearchAssetAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<AssetItem>();

        try
        {
            var results = await AssetInfoService.SearchAsync(query, cancellationToken);
            return results.Select(asset => new AssetItem { Name = asset.Name, Code = asset.Code }).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "搜索虚拟币时出错，查询：{Query}", query);
            return new List<AssetItem>();
        }
    }

    /// <summary>
    /// 获取热门虚拟币
    /// </summary>
    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        try
        {
            return await AssetInfoService.GetHotAssetsAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取热门虚拟币时出错");
            return new List<HotAsset>();
        }
    }

    /// <summary>
    /// 获取最近查看的虚拟币
    /// </summary>
    public List<AssetItem> GetRecentAssets()
    {
        try
        {
            return HistoryService.GetHistory();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取最近查看的虚拟币时出错");
            return new List<AssetItem>();
        }
    }

    /// <summary>
    /// 添加到最近查看
    /// </summary>
    public void AddToRecentAssets(AssetItem asset)
    {
        try
        {
            HistoryService.AddHistory(asset);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "添加虚拟币到最近查看时出错: {Code}", asset.Code);
        }
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    public async Task<bool> AddToFavoriteAsync(object assetParameter)
    {
        try
        {
            if (assetParameter is not ViewModels.AssetNavigationParameter parameter)
            {
                _logger?.LogWarning("添加到收藏失败：参数类型不匹配");
                return false;
            }

            // 检查是否已收藏（虚拟币使用空字符串作为market）
            if (FavoriteService.IsFavorite(parameter.Code, ""))
            {
                await _dialogService.ShowMessageAsync("提示", "该虚拟币已在收藏列表中");
                return false;
            }

            // 添加到收藏
            FavoriteService.AddFavorite(parameter.Code, "");
            await _dialogService.ShowMessageAsync("成功", "已添加到收藏");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "添加虚拟币到收藏时出错");
            await _dialogService.ShowMessageAsync("错误", $"添加到收藏失败：{ex.Message}");
            return false;
        }
    }
}






