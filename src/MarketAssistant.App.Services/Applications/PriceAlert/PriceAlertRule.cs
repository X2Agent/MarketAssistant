using System.ComponentModel;
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

    /// <summary>
    /// 是否基于涨跌幅触发（目标值单位为百分比）
    /// </summary>
    public bool IsPercentCondition =>
        Condition is AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow;
}

public enum AlertCondition
{
    [Description("涨破")]
    PriceAbove,

    [Description("跌破")]
    PriceBelow,

    [Description("涨幅超")]
    ChangePercentAbove,

    [Description("跌幅超")]
    ChangePercentBelow
}
