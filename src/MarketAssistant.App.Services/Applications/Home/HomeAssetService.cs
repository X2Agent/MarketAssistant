using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.History;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Home;

/// <summary>
/// 首页资产服务：封装搜索、热门、历史与收藏流程。
/// 通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型，
/// 构造时一次性解析同市场的 keyed 依赖。
/// </summary>
public sealed class HomeAssetService : IHomeAssetService
{
    private readonly IAssetInfoService _assetInfoService;
    private readonly IAssetHistoryService _historyService;
    private readonly IFavoriteService _favoriteService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<HomeAssetService> _logger;
    private readonly MarketType _marketType;

    public HomeAssetService(
        [ServiceKey] MarketType marketType,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILogger<HomeAssetService> logger)
    {
        _marketType = marketType;
        _assetInfoService = serviceProvider.GetRequiredKeyedService<IAssetInfoService>(marketType);
        _historyService = serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(marketType);
        _favoriteService = serviceProvider.GetRequiredKeyedService<IFavoriteService>(marketType);
        _dialogService = dialogService;
        _logger = logger;
    }

    public async Task<List<AssetItem>> SearchAssetAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var results = await _assetInfoService.SearchAsync(query, cancellationToken);
        return results.Select(asset => new AssetItem { Name = asset.Name, Code = asset.Code }).ToList();
    }

    public Task<List<HotAsset>> GetHotAssetsAsync()
        => _assetInfoService.GetHotAssetsAsync();

    public Task<List<AssetItem>> GetRecentAssetsAsync(CancellationToken cancellationToken = default)
        => _historyService.GetHistoryAsync(cancellationToken);

    public async Task AddToRecentAssetsAsync(AssetItem asset, CancellationToken cancellationToken = default)
        => await _historyService.AddHistoryAsync(asset, cancellationToken);

    public async Task<bool> AddToFavoriteAsync(object assetParameter)
    {
        if (!TryMapFavoriteAsset(assetParameter, out var code, out var market, out var assetName, out var errorMessage))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                _logger.LogWarning("添加到收藏失败: {Reason}", errorMessage);
                await _dialogService.ShowMessageAsync("错误", errorMessage);
            }
            return false;
        }

        if (await _favoriteService.IsFavoriteAsync(code, market))
        {
            var alreadyMsg = _marketType == MarketType.Crypto
                ? "该虚拟币已在收藏列表中"
                : "该资产已在收藏列表中";
            await _dialogService.ShowMessageAsync("提示", alreadyMsg);
            return false;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "添加收藏",
            $"确定要将 {assetName} 添加到收藏列表吗？",
            "确认",
            "取消");

        if (!confirmed)
            return false;

        await _favoriteService.AddFavoriteAsync(code, market);
        await _dialogService.ShowMessageAsync("收藏成功", $"已将 {assetName} 添加到收藏列表");
        return true;
    }

    private bool TryMapFavoriteAsset(
        object assetParameter,
        out string code,
        out string market,
        out string assetName,
        out string? errorMessage)
    {
        errorMessage = null;
        code = string.Empty;
        market = string.Empty;
        assetName = string.Empty;

        if (assetParameter is HotAsset hotAsset)
        {
            assetName = hotAsset.Name;
            code = hotAsset.Code;
            market = _marketType == MarketType.AShare ? hotAsset.Market : string.Empty;
            return true;
        }

        if (assetParameter is AssetItem assetItem)
        {
            assetName = assetItem.Name;
            code = assetItem.Code;

            if (_marketType == MarketType.AShare)
            {
                if (code.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                    || code.StartsWith("sz", StringComparison.OrdinalIgnoreCase))
                {
                    market = code[..2].ToUpperInvariant();
                    code = code[2..];
                }
            }

            return true;
        }

        errorMessage = "添加到收藏失败：参数类型不匹配";
        return false;
    }
}
