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

        var results = await AssetInfoService.SearchAsync(query, cancellationToken);
        return results.Select(asset => new AssetItem { Name = asset.Name, Code = asset.Code }).ToList();
    }

    /// <summary>
    /// 获取热门虚拟币
    /// </summary>
    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        // FriendlyException会被UI层捕获并显示，不在此处处理
        return await AssetInfoService.GetHotAssetsAsync();
    }

    /// <summary>
    /// 获取最近查看的虚拟币
    /// </summary>
    public List<AssetItem> GetRecentAssets()
    {
        return HistoryService.GetHistory();
    }

    /// <summary>
    /// 添加到最近查看
    /// </summary>
    public void AddToRecentAssets(AssetItem asset)
    {
        HistoryService.AddHistory(asset);
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    public async Task<bool> AddToFavoriteAsync(object assetParameter)
    {
        string assetName = "";
        string code = "";

        if (assetParameter is HotAsset hotAsset)
        {
            assetName = hotAsset.Name;
            code = hotAsset.Code;
        }
        else if (assetParameter is AssetItem assetItem)
        {
            assetName = assetItem.Name;
            code = assetItem.Code;
        }
        else
        {
            _logger?.LogWarning("添加到收藏失败：参数类型不匹配");
            await _dialogService.ShowMessageAsync("错误", "添加到收藏失败：参数类型不匹配");
            return false;
        }

        // 检查是否已收藏（虚拟币使用空字符串作为market）
        if (FavoriteService.IsFavorite(code, ""))
        {
            await _dialogService.ShowMessageAsync("提示", "该虚拟币已在收藏列表中");
            return false;
        }

        bool confirmed = await _dialogService.ShowConfirmationAsync(
            "添加收藏",
            $"确定要将 {assetName} 添加到收藏列表吗？",
            "确认",
            "取消");

        if (confirmed)
        {
            FavoriteService.AddFavorite(code, "");
            await _dialogService.ShowMessageAsync("收藏成功", $"已将 {assetName} 添加到收藏列表");
            return true;
        }

        return false;
    }
}






