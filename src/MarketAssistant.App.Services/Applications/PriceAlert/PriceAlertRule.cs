using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.PriceAlert;

/// <summary>
/// 价格预警规则
/// </summary>
public class PriceAlertRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string AssetCode { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public MarketType MarketType { get; set; } = MarketType.Crypto;
    public AlertCondition Condition { get; set; }
    public decimal TargetPrice { get; set; }
    public bool Triggered { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AlertCondition
{
    PriceAbove,
    PriceBelow
}
