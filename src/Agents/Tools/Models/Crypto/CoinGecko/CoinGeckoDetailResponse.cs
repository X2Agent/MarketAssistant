using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Crypto.CoinGecko;

public class CoinGeckoDetailResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public CoinDescription? Description { get; set; }

    [JsonPropertyName("market_cap_rank")]
    public int? MarketCapRank { get; set; }

    [JsonPropertyName("market_data")]
    public CoinMarketData? MarketData { get; set; }
}

public class CoinDescription
{
    [JsonPropertyName("en")]
    public string En { get; set; } = string.Empty;
}

public class CoinMarketData
{
    [JsonPropertyName("current_price")]
    public CurrencyValue? CurrentPrice { get; set; }

    [JsonPropertyName("market_cap")]
    public CurrencyValue? MarketCap { get; set; }

    [JsonPropertyName("fully_diluted_valuation")]
    public CurrencyValue? FullyDilutedValuation { get; set; }

    [JsonPropertyName("total_supply")]
    public decimal? TotalSupply { get; set; }

    [JsonPropertyName("circulating_supply")]
    public decimal? CirculatingSupply { get; set; }

    [JsonPropertyName("max_supply")]
    public decimal? MaxSupply { get; set; }

    [JsonPropertyName("total_volume")]
    public CurrencyValue? TotalVolume { get; set; }

    [JsonPropertyName("price_change_24h")]
    public decimal? PriceChange24h { get; set; }

    [JsonPropertyName("price_change_percentage_24h")]
    public decimal? PriceChangePercentage24h { get; set; }

    [JsonPropertyName("price_change_percentage_7d")]
    public decimal? PriceChangePercentage7d { get; set; }

    [JsonPropertyName("price_change_percentage_30d")]
    public decimal? PriceChangePercentage30d { get; set; }
}

public class CurrencyValue
{
    [JsonPropertyName("usd")]
    public double Usd { get; set; }
}
