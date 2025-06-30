using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.Stocks;

/// <summary>
/// 股票搜索历史记录管理类
/// </summary>
public class StockSearchHistory
{
    private const string PreferenceKey = "RecentViewedStocks";
    private const int MaxHistoryCount = 10; // 最多保存10条历史记录
    private readonly ILogger<StockSearchHistory> _logger;

    public StockSearchHistory(ILogger<StockSearchHistory> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 添加一条股票访问记录
    /// </summary>
    /// <param name="stock">股票信息</param>
    public void AddSearchHistory(StockItem stock)
    {
        if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
            return;

        var historyList = GetSearchHistory();

        // 如果已存在相同代码的记录，先移除旧记录
        var existingItem = historyList.FirstOrDefault(x => x.Code == stock.Code);
        if (existingItem != null)
        {
            historyList.Remove(existingItem);
        }

        // 添加到列表开头（最新的记录放在最前面）
        historyList.Insert(0, stock);

        // 如果超过最大数量，移除最旧的记录
        if (historyList.Count > MaxHistoryCount)
        {
            historyList.RemoveAt(historyList.Count - 1);
        }

        // 保存到本地存储
        SaveSearchHistory(historyList);
    }

    /// <summary>
    /// 获取搜索历史记录列表
    /// </summary>
    /// <returns>股票历史记录列表</returns>
    public List<StockItem> GetSearchHistory()
    {
        try
        {
            var json = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return new List<StockItem>();

            var historyList = JsonSerializer.Deserialize<List<StockItem>>(json);
            return historyList ?? new List<StockItem>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取搜索历史记录时出错: {Message}", ex.Message);
            return new List<StockItem>();
        }
    }

    /// <summary>
    /// 清空搜索历史记录
    /// </summary>
    public void ClearSearchHistory()
    {
        SaveSearchHistory(new List<StockItem>());
    }

    /// <summary>
    /// 保存搜索历史记录到本地存储
    /// </summary>
    /// <param name="historyList">要保存的历史记录列表</param>
    private void SaveSearchHistory(List<StockItem> historyList)
    {
        try
        {
            var json = JsonSerializer.Serialize(historyList);
            Preferences.Default.Set(PreferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "保存搜索历史记录时出错: {Message}", ex.Message);
        }
    }
}
