using System.Text.Json.Serialization;

namespace MarketAssistant.Services.Data;

public class CoinGeckoTickersResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tickers")]
    public List<TickerData> Tickers { get; set; } = [];
}

public class TickerData
{
    [JsonPropertyName("base")]
    public string Base { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("market")]
    public MarketInfo? Market { get; set; }

    [JsonPropertyName("converted_volume")]
    public ConvertedVolume? ConvertedVolume { get; set; }
}

public class MarketInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;
}

public class ConvertedVolume
{
    [JsonPropertyName("usd")]
    public double Usd { get; set; }
}
