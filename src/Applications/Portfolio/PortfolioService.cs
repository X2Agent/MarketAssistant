using System.Text.Json;
using MarketAssistant.Applications.Portfolio.Models;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Portfolio;

/// <summary>
/// 持仓组合管理服务，负责买入/卖出记录与盈亏计算
/// </summary>
public class PortfolioService
{
    private const string PositionsKey = "Portfolio_Positions";
    private const string TradesKey = "Portfolio_Trades";
    private readonly ILogger<PortfolioService> _logger;
    private List<PortfolioPosition> _positions = [];
    private List<TradeRecord> _trades = [];

    public IReadOnlyList<PortfolioPosition> Positions => _positions;
    public IReadOnlyList<TradeRecord> Trades => _trades;
    public event Action? PortfolioChanged;

    public decimal TotalMarketValue => _positions.Sum(p => p.MarketValue);
    public decimal TotalCost => _positions.Sum(p => p.TotalCost);
    public decimal TotalProfitLoss => TotalMarketValue - TotalCost;

    public PortfolioService(ILogger<PortfolioService> logger)
    {
        _logger = logger;
        LoadData();
    }

    /// <summary>
    /// 买入资产，已有持仓则加仓并重新计算均价
    /// </summary>
    public void Buy(string assetCode, string assetName, MarketType marketType, decimal price, decimal quantity)
    {
        var position = _positions.FirstOrDefault(p => p.AssetCode == assetCode);

        if (position != null)
        {
            var totalQty = position.Quantity + quantity;
            position.AverageCost = (position.TotalCost + price * quantity) / totalQty;
            position.Quantity = totalQty;
        }
        else
        {
            position = new PortfolioPosition
            {
                AssetCode = assetCode,
                AssetName = assetName,
                MarketType = marketType,
                Quantity = quantity,
                AverageCost = price
            };
            _positions.Add(position);
        }

        _trades.Add(new TradeRecord
        {
            PositionId = position.Id,
            AssetCode = assetCode,
            AssetName = assetName,
            MarketType = marketType,
            Direction = TradeDirection.Buy,
            Price = price,
            Quantity = quantity
        });

        SaveData();
        PortfolioChanged?.Invoke();
    }

    /// <summary>
    /// 卖出资产，持仓不足时返回 false
    /// </summary>
    public bool Sell(string assetCode, decimal price, decimal quantity)
    {
        var position = _positions.FirstOrDefault(p => p.AssetCode == assetCode);
        if (position == null || position.Quantity < quantity)
            return false;

        position.Quantity -= quantity;

        _trades.Add(new TradeRecord
        {
            PositionId = position.Id,
            AssetCode = assetCode,
            AssetName = position.AssetName,
            MarketType = position.MarketType,
            Direction = TradeDirection.Sell,
            Price = price,
            Quantity = quantity
        });

        if (position.Quantity <= 0)
            _positions.Remove(position);

        SaveData();
        PortfolioChanged?.Invoke();
        return true;
    }

    public void RemovePosition(string positionId)
    {
        _positions.RemoveAll(p => p.Id == positionId);
        SaveData();
        PortfolioChanged?.Invoke();
    }

    public void UpdateCurrentPrice(string assetCode, decimal currentPrice)
    {
        var position = _positions.FirstOrDefault(p => p.AssetCode == assetCode);
        if (position != null)
            position.CurrentPrice = currentPrice;
    }

    private void LoadData()
    {
        try
        {
            var posJson = Preferences.Default.Get(PositionsKey, string.Empty);
            if (!string.IsNullOrEmpty(posJson))
                _positions = JsonSerializer.Deserialize<List<PortfolioPosition>>(posJson) ?? [];

            var tradeJson = Preferences.Default.Get(TradesKey, string.Empty);
            if (!string.IsNullOrEmpty(tradeJson))
                _trades = JsonSerializer.Deserialize<List<TradeRecord>>(tradeJson) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载持仓数据失败");
        }
    }

    private void SaveData()
    {
        try
        {
            Preferences.Default.Set(PositionsKey, JsonSerializer.Serialize(_positions));
            Preferences.Default.Set(TradesKey, JsonSerializer.Serialize(_trades));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存持仓数据失败");
        }
    }
}
