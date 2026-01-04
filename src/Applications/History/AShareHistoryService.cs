using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.History;

/// <summary>
/// A股资产历史记录服务实现
/// </summary>
public class AShareHistoryService : IAssetHistoryService
{
    private const string PreferenceKey = "RecentAssets_AShare";
    private const int MaxHistoryCount = 10;
    private readonly ILogger<AShareHistoryService> _logger;

    public AShareHistoryService(ILogger<AShareHistoryService> logger)
    {
        _logger = logger;
    }

    public void AddHistory(AssetItem asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Code))
            return;

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
    }

    public List<AssetItem> GetHistory()
    {
        try
        {
            var json = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return new List<AssetItem>();

            var historyList = JsonSerializer.Deserialize<List<AssetItem>>(json);
            return historyList ?? new List<AssetItem>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取历史记录时出错: {Message}", ex.Message);
            return new List<AssetItem>();
        }
    }

    public void ClearHistory()
    {
        SaveHistory(new List<AssetItem>());
    }

    private void SaveHistory(List<AssetItem> historyList)
    {
        try
        {
            var json = JsonSerializer.Serialize(historyList);
            Preferences.Default.Set(PreferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "保存历史记录时出错: {Message}", ex.Message);
        }
    }
}






