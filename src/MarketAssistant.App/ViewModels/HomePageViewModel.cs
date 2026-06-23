using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.ViewModels.Home;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 首页ViewModel
/// </summary>
public partial class HomePageViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// 搜索功能ViewModel
    /// </summary>
    public HomeSearchViewModel Search { get; }

    /// <summary>
    /// 热门资产ViewModel
    /// </summary>
    public HotAssetsViewModel HotAssets { get; }

    /// <summary>
    /// 最近查看ViewModel
    /// </summary>
    public RecentAssetsViewModel RecentAssets { get; }

    /// <summary>
    /// 新闻快讯ViewModel
    /// </summary>
    public TelegraphNewsViewModel News { get; }

    /// <summary>
    /// 构造函数（使用依赖注入）
    /// </summary>
    public HomePageViewModel(
        HomeSearchViewModel searchViewModel,
        HotAssetsViewModel hotAssetsViewModel,
        RecentAssetsViewModel recentAssetsViewModel,
        TelegraphNewsViewModel newsViewModel,
        ILogger<HomePageViewModel> logger) : base(logger)
    {
        Search = searchViewModel;
        HotAssets = hotAssetsViewModel;
        RecentAssets = recentAssetsViewModel;
        News = newsViewModel;

        // 订阅子ViewModel事件
        Search.AssetSelected += OnAssetSelected;
        HotAssets.HotAssetSelected += OnHotAssetSelected;
        RecentAssets.RecentAssetSelected += OnRecentAssetSelected;
    }

    /// <summary>
    /// 处理搜索资产选择事件
    /// </summary>
    private void OnAssetSelected(object? sender, AssetItem asset)
    {
        NavigateToAsset(asset);
    }

    /// <summary>
    /// 处理热门资产选择事件
    /// </summary>
    private void OnHotAssetSelected(object? sender, HotAsset asset)
    {
        // 根据市场类型决定是否拼接市场代码
        var assetCode = asset.MarketType == Infrastructure.Core.MarketType.Crypto
            ? asset.Code.ToLower()
            : $"{asset.Market}{asset.Code}".ToLower();

        // 解析价格信息
        decimal? currentPrice = decimal.TryParse(asset.CurrentPrice, out var price) ? price : null;
        decimal? changePercent = decimal.TryParse(asset.ChangePercentage?.TrimEnd('%'), out var percent) ? percent : null;

        // 传递完整的基本信息，避免详情页等待
        var parameter = new AssetNavigationParameter(
            assetCode,
            asset.Name,
            currentPrice,
            changePercent
        );

        WeakReferenceMessenger.Default.Send(new NavigationMessage("Asset", parameter));
        Logger?.LogInformation($"导航到资产详情页: {assetCode}");

        // 异步添加到最近查看
        var assetItem = new AssetItem { Name = asset.Name, Code = assetCode };
        _ = Task.Run(() => RecentAssets.AddToRecentAssets(assetItem));
    }

    /// <summary>
    /// 处理最近资产选择事件
    /// </summary>
    private void OnRecentAssetSelected(object? sender, AssetItem asset)
    {
        NavigateToAsset(asset);
    }

    /// <summary>
    /// 导航到资产详情页
    /// </summary>
    private void NavigateToAsset(AssetItem assetItem, decimal? currentPrice = null, decimal? changePercent = null)
    {
        // 立即发送导航消息，不阻塞UI
        WeakReferenceMessenger.Default.Send(
            new NavigationMessage("Asset", new AssetNavigationParameter(
                assetItem.Code,
                assetItem.Name,
                currentPrice,
                changePercent)));

        Logger?.LogInformation($"导航到资产详情页: {assetItem.Code}");

        // 异步添加到最近查看，不阻塞导航
        _ = Task.Run(() => RecentAssets.AddToRecentAssets(assetItem));
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        // 取消事件订阅
        Search.AssetSelected -= OnAssetSelected;
        HotAssets.HotAssetSelected -= OnHotAssetSelected;
        RecentAssets.RecentAssetSelected -= OnRecentAssetSelected;

        // 释放子ViewModel资源
        HotAssets.Dispose();
        RecentAssets.Dispose();
        News.Dispose();

        GC.SuppressFinalize(this);
    }
}