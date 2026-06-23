using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.History;

/// <summary>
/// 资产历史记录服务：封装本地存储与容量控制逻辑。
/// 通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型。
/// </summary>
public sealed class AssetHistoryService : IAssetHistoryService
{
    private const int MaxHistoryCount = 10;
    private readonly ILogger<AssetHistoryService> _logger;
    private readonly string _preferenceKey;
    private readonly string _marketLabel;

    public AssetHistoryService([ServiceKey] MarketType marketType, ILogger<AssetHistoryService> logger)
    {
        _logger = logger;
        _preferenceKey = PreferenceKeys.GetRecentAssetsKey(marketType);
        _marketLabel = marketType switch
        {
            MarketType.AShare => "A股",
            MarketType.Crypto => "虚拟币",
            _ => marketType.ToString()
        };
    }

    public void AddHistory(AssetItem asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Code))
        {
            return;
        }

        var historyList = GetHistory();
        var existingItem = historyList.FirstOrDefault(x => x.Code == asset.Code);
        if (existingItem != null)
        {
            historyList.Remove(existingItem);
        }

        historyList.Insert(0, asset);
        if (historyList.Count > MaxHistoryCount)
        {
            historyList.RemoveAt(historyList.Count - 1);
        }

        SaveHistory(historyList);
        _logger.LogInformation("已添加{Market}到历史记录: {Code}", _marketLabel, asset.Code);
    }

    public List<AssetItem> GetHistory()
    {
        try
        {
            var json = Preferences.Default.Get(_preferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<AssetItem>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取历史记录时出错: {Message}", ex.Message);
            return [];
        }
    }

    public void ClearHistory()
    {
        SaveHistory([]);
        _logger.LogInformation("已清空{Market}历史记录", _marketLabel);
    }

    private void SaveHistory(List<AssetItem> historyList)
    {
        try
        {
            var json = JsonSerializer.Serialize(historyList);
            Preferences.Default.Set(_preferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存历史记录时出错: {Message}", ex.Message);
        }
    }
}
