using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.Portfolio.Models;

/// <summary>
/// 持仓记录
/// </summary>
public class PortfolioPosition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string AssetCode { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public MarketType MarketType { get; set; }
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public decimal CurrentPrice { get; set; }

    public decimal TotalCost => Quantity * AverageCost;
    public decimal MarketValue => Quantity * CurrentPrice;
    public decimal ProfitLoss => MarketValue - TotalCost;
    public decimal ProfitLossPercent => TotalCost > 0
        ? Math.Round((MarketValue - TotalCost) / TotalCost * 100, 2)
        : 0;
}

/// <summary>
/// 交易记录
/// </summary>
public class TradeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string PositionId { get; set; } = string.Empty;
    public string AssetCode { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public MarketType MarketType { get; set; }
    public TradeDirection Direction { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public DateTime TradeTime { get; set; } = DateTime.UtcNow;
}

public enum TradeDirection
{
    Buy,
    Sell
}
