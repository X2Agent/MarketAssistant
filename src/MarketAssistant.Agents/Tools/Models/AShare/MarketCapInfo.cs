namespace MarketAssistant.Agents.Tools.Models.AShare;

public class MarketCapInfo
{
    public string Symbol { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal CurrentPrice { get; set; }

    public decimal MarketCap { get; set; }

    public decimal? FullyDilutedValuation { get; set; }

    public int MarketCapRank { get; set; }

    public decimal Volume24h { get; set; }

    public decimal? PriceChange24h { get; set; }

    public string UpdatedAt { get; set; } = string.Empty;
}
