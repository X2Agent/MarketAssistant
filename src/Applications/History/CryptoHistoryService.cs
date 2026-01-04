using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.History;

/// <summary>
/// 虚拟币资产历史记录服务实现
/// </summary>
public class CryptoHistoryService : IAssetHistoryService
{
    private const string PreferenceKey = "RecentAssets_Crypto";
    private const int MaxHistoryCount = 10;
    private readonly ILogger<CryptoHistoryService> _logger;

    public CryptoHistoryService(ILogger<CryptoHistoryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 添加虚拟币到历史记录
    /// </summary>
    public void AddHistory(AssetItem asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Code))
            return;

        var historyList = GetHistory();

        // 如果已存在，先移除
        var existingItem = historyList.FirstOrDefault(x => x.Code == asset.Code);
        if (existingItem != null)
        {
            historyList.Remove(existingItem);
        }

        // 插入到最前面
        historyList.Insert(0, asset);

        // 保持最多10条记录
        if (historyList.Count > MaxHistoryCount)
        {
            historyList.RemoveAt(historyList.Count - 1);
        }

        SaveHistory(historyList);
        _logger.LogInformation("已添加虚拟币到历史记录: {Code}", asset.Code);
    }

    /// <summary>
    /// 获取历史记录
    /// </summary>
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
            _logger?.LogError(ex, "获取虚拟币历史记录时出错: {Message}", ex.Message);
            return new List<AssetItem>();
        }
    }

    /// <summary>
    /// 清空历史记录
    /// </summary>
    public void ClearHistory()
    {
        SaveHistory(new List<AssetItem>());
        _logger.LogInformation("已清空虚拟币历史记录");
    }

    /// <summary>
    /// 获取最近查看的虚拟币（与GetHistory功能相同）
    /// </summary>
    public List<AssetItem> GetRecentAssets()
    {
        return GetHistory();
    }

    /// <summary>
    /// 添加到最近查看（与AddHistory功能相同）
    /// </summary>
    public void AddToRecentAssets(AssetItem asset)
    {
        AddHistory(asset);
    }

    /// <summary>
    /// 保存历史记录到本地存储
    /// </summary>
    private void SaveHistory(List<AssetItem> historyList)
    {
        try
        {
            var json = JsonSerializer.Serialize(historyList);
            Preferences.Default.Set(PreferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "保存虚拟币历史记录时出错: {Message}", ex.Message);
        }
    }
}
