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
/// A股首页服务实现
/// </summary>
public class AShareHomeService : IHomeAssetService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;
    private readonly ILogger<AShareHomeService> _logger;

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

    public AShareHomeService(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILogger<AShareHomeService> logger)
    {
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _logger = logger;
    }

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
            _logger?.LogError(ex, "搜索资产时出错，查询：{Query}", query);
            return new List<AssetItem>();
        }
    }

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        try
        {
            return await AssetInfoService.GetHotAssetsAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取热门资产时出错");
            return new List<HotAsset>();
        }
    }

    public List<AssetItem> GetRecentAssets()
    {
        try
        {
            return HistoryService.GetHistory();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取最近查看资产时出错");
            return new List<AssetItem>();
        }
    }

    public void AddToRecentAssets(AssetItem asset)
    {
        try
        {
            HistoryService.AddHistory(asset);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "添加到最近查看时出错，资产：{AssetName}", asset?.Name);
        }
    }

    public async Task<bool> AddToFavoriteAsync(object assetParameter)
    {
        try
        {
            string assetName = "";
            string code = "";
            string market = "";

            if (assetParameter is HotAsset hotAsset)
            {
                assetName = hotAsset.Name;
                code = hotAsset.Code;
                market = hotAsset.Market;
            }
            else if (assetParameter is AssetItem assetItem)
            {
                assetName = assetItem.Name;
                code = assetItem.Code;

                // 尝试从资产代码中提取市场代码
                if (code.StartsWith("sh") || code.StartsWith("sz"))
                {
                    market = code.Substring(0, 2).ToUpper();
                    code = code.Substring(2);
                }
            }
            else
            {
                return false;
            }

            bool confirmed = await _dialogService.ShowConfirmationAsync(
                "添加收藏",
                $"确定要将 {assetName} 添加到收藏列表吗？",
                "确认",
                "取消");

            if (confirmed)
            {
                FavoriteService.AddFavorite(code, market);
                await _dialogService.ShowMessageAsync("收藏成功", $"已将 {assetName} 添加到收藏列表");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "添加收藏时出错");
            await _dialogService.ShowMessageAsync("收藏失败", "添加收藏时发生错误，请稍后重试");
            return false;
        }
    }
}

