using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;

namespace MarketAssistant.ViewModels.Home;

/// <summary>首页热门资产卡片的展示项：包装 <see cref="HotAsset"/>，并持有名次与自选状态。</summary>
public partial class HotAssetItem(HotAsset asset, int rank) : ObservableObject
{
    public HotAsset Asset { get; } = asset;

    public string RankText { get; } = $"#{rank}";

    /// <summary>收藏存储使用的归一化代码，与收藏页、详情页同一规则。</summary>
    public string FavoriteCode { get; } = FavoriteCodeNormalizer.Normalize(asset.Code, asset.MarketType);

    /// <summary>收藏存储使用的来源标记：A 股为交易所前缀，虚拟币为空。</summary>
    public string FavoriteMarket { get; } = asset.MarketType == MarketType.AShare ? asset.Market : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteToolTip))]
    private bool _isFavorite;

    public string FavoriteToolTip => IsFavorite ? "取消自选" : "加入自选";
}
