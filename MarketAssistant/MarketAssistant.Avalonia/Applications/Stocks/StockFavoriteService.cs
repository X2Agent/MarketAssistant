using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.Stocks;

public record StockFavoritesChanged();

public class StockFavoriteService
{
    private const string PreferenceKey = "FavoriteStocks";
    private readonly StockService _stockService;
    private readonly ILogger<StockFavoriteService> _logger;

    public StockFavoriteService(StockService stockService, ILogger<StockFavoriteService> logger)
    {
        _stockService = stockService;
        _logger = logger;
    }

    /// <summary>
    /// 添加股票到收藏
    /// </summary>
    /// <param name="code">股票代码</param>
    /// <param name="market">市场代码</param>
    /// <param name="name">股票名称</param>
    public void AddFavorite(string code, string market)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(market))
            return;

        var favoriteList = GetFavoritesCodes();

        // 检查是否已经收藏过
        var existingItem = favoriteList.FirstOrDefault(x => x.Code == code && x.Market == market);
        if (existingItem != null)
            return; // 已经收藏过，不重复添加

        // 添加到收藏列表
        favoriteList.Add(new FavoriteStock { Code = code, Market = market });

        // 保存到本地存储
        SaveFavorites(favoriteList);
        WeakReferenceMessenger.Default.Send(new StockFavoritesChanged());
    }

    /// <summary>
    /// 从收藏中移除股票
    /// </summary>
    /// <param name="stock">要移除的股票</param>
    public void RemoveFavorite(string code, string market)
    {
        var favoriteList = GetFavoritesCodes();

        // 查找并移除匹配的股票
        var itemToRemove = favoriteList.FirstOrDefault(x => x.Code == code && x.Market == market);
        if (itemToRemove != null)
        {
            favoriteList.Remove(itemToRemove);
            SaveFavorites(favoriteList);
            WeakReferenceMessenger.Default.Send(new StockFavoritesChanged());
        }
    }

    /// <summary>
    /// 检查股票是否已收藏
    /// </summary>
    /// <param name="code">股票代码</param>
    /// <param name="market">市场代码</param>
    /// <returns>是否已收藏</returns>
    public bool IsFavorite(string code, string market)
    {
        var favoriteList = GetFavoritesCodes();
        return favoriteList.Any(x => x.Code == code && x.Market == market);
    }

    /// <summary>
    /// 获取所有收藏的股票代码
    /// </summary>
    /// <returns>收藏的股票代码列表</returns>
    public List<FavoriteStock> GetFavoritesCodes()
    {
        try
        {
            var json = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return new List<FavoriteStock>();

            var favoriteList = JsonSerializer.Deserialize<List<FavoriteStock>>(json);
            return favoriteList ?? new List<FavoriteStock>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取收藏股票时出错: {ex.Message}");
            return new List<FavoriteStock>();
        }
    }

    /// <summary>
    /// 获取所有收藏的股票（包含最新数据）
    /// </summary>
    /// <returns>收藏的股票列表（带最新数据）</returns>
    public async Task<List<StockInfo>> GetFavoritesWithLatestDataAsync(CancellationToken cancellationToken = default)
    {
        var favoritesCodes = GetFavoritesCodes();
        if (favoritesCodes.Count == 0)
            return new();

        // 创建更新后的收藏股票列表
        var stockInfos = new List<StockInfo>();

        try
        {
            // 创建任务列表，为每个收藏的股票并行获取最新数据
            var tasks = new List<Task<StockInfo>>();

            // 为每个收藏的股票创建一个任务
            foreach (var favorite in favoritesCodes)
            {
                var task = Task.Run(async () =>
                {
                    try
                    {
                        // 使用GetStockInfoAsync获取最新股票数据
                        return await _stockService.GetStockInfoAsync(favorite.Code, favorite.Market, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"获取股票 {favorite.Code} 最新数据时出错: {ex.Message}");
                        // 发生错误时返回基本信息
                        return new StockInfo { Code = favorite.Code, Market = favorite.Market, Name = $"{favorite.Market}.{favorite.Code}" };
                    }
                });

                tasks.Add(task);
            }

            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);

            // 将结果添加到列表中
            stockInfos.AddRange(results.Where(r => r != null));

            return stockInfos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取收藏股票最新数据时出错: {ex.Message}");
            return stockInfos; // 返回原始数据，不带最新价格和涨跌幅
        }
    }

    /// <summary>
    /// 清空所有收藏
    /// </summary>
    public void ClearFavorites()
    {
        SaveFavorites(new List<FavoriteStock>());
        WeakReferenceMessenger.Default.Send(new StockFavoritesChanged());
    }

    /// <summary>
    /// 保存收藏列表到本地存储
    /// </summary>
    /// <param name="favoriteList">要保存的收藏列表</param>
    private void SaveFavorites(List<FavoriteStock> favoriteList)
    {
        try
        {
            var json = JsonSerializer.Serialize(favoriteList);
            Preferences.Default.Set(PreferenceKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"保存收藏股票时出错: {ex.Message}");
        }
    }
}
