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

        var results = await AssetInfoService.SearchAsync(query, cancellationToken);
        return results.Select(asset => new AssetItem { Name = asset.Name, Code = asset.Code }).ToList();
    }

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        return await AssetInfoService.GetHotAssetsAsync();
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
}

