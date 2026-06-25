using System.Text.Json.Serialization;

namespace MarketAssistant.Services.Data;

public class CoinGeckoSearchResponse
{
    [JsonPropertyName("coins")]
    public List<CoinSearchResult> Coins { get; set; } = [];
}

public class CoinSearchResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("market_cap_rank")]
    public int? MarketCapRank { get; set; }
}
