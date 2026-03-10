using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.History;

/// <summary>
/// 资产历史记录服务基类，封装本地存储与容量控制逻辑。
/// </summary>
public abstract class AssetHistoryServiceBase : IAssetHistoryService
{
    private const int MaxHistoryCount = 10;
    private readonly ILogger _logger;

    protected AssetHistoryServiceBase(ILogger logger)
    {
        _logger = logger;
    }

    protected abstract string PreferenceKey { get; }

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
        LogHistoryAdded(asset);
    }

    public List<AssetItem> GetHistory()
    {
        try
        {
            var json = Preferences.Default.Get(PreferenceKey, string.Empty);
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
        LogHistoryCleared();
    }

    protected virtual void LogHistoryAdded(AssetItem asset)
    {
    }

    protected virtual void LogHistoryCleared()
    {
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
            _logger.LogError(ex, "保存历史记录时出错: {Message}", ex.Message);
        }
    }
}