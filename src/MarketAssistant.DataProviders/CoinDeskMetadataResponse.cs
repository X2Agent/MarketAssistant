using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// CoinDesk 元数据响应根对象
/// </summary>
public class CoinDeskMetadataResponse
{
    [JsonPropertyName("Data")]
    public Dictionary<string, CoinDeskAssetData>? Data { get; set; }
}

/// <summary>
/// CoinDesk 资产数据
/// </summary>
public class CoinDeskAssetData
{
    [JsonPropertyName("SYMBOL")]
    public string? Symbol { get; set; }

    [JsonPropertyName("NAME")]
    public string? Name { get; set; }

    [JsonPropertyName("URI")]
    public string? Uri { get; set; }

    [JsonPropertyName("ASSET_TYPE")]
    public string? AssetType { get; set; }

    [JsonPropertyName("ASSET_ISSUER_NAME")]
    public string? AssetIssuerName { get; set; }

    [JsonPropertyName("ASSET_ALTERNATIVE_IDS")]
    public List<CoinDeskAlternativeId>? AssetAlternativeIds { get; set; }

    [JsonPropertyName("ASSET_DESCRIPTION_SNIPPET")]
    public string? AssetDescriptionSnippet { get; set; }

    [JsonPropertyName("ASSET_DESCRIPTION")]
    public string? AssetDescription { get; set; }

    [JsonPropertyName("ASSET_SECURITY_METRICS")]
    public List<CoinDeskSecurityMetric>? AssetSecurityMetrics { get; set; }

    [JsonPropertyName("SUPPLY_MAX")]
    public decimal? SupplyMax { get; set; }

    [JsonPropertyName("SUPPLY_ISSUED")]
    public decimal? SupplyIssued { get; set; }

    [JsonPropertyName("SUPPLY_TOTAL")]
    public decimal? SupplyTotal { get; set; }

    [JsonPropertyName("SUPPLY_CIRCULATING")]
    public decimal? SupplyCirculating { get; set; }

    [JsonPropertyName("SUPPLY_BURNT")]
    public decimal? SupplyBurnt { get; set; }

    [JsonPropertyName("PRICE_USD")]
    public decimal? PriceUsd { get; set; }

    [JsonPropertyName("PRICE_USD_SOURCE")]
    public string? PriceUsdSource { get; set; }

    [JsonPropertyName("TOTAL_MKT_CAP_USD")]
    public decimal? TotalMktCapUsd { get; set; }

    [JsonPropertyName("CIRCULATING_MKT_CAP_USD")]
    public decimal? CirculatingMktCapUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_24_HOUR_QUOTE_VOLUME_USD")]
    public decimal? SpotMoving24HourQuoteVolumeUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_7_DAY_QUOTE_VOLUME_USD")]
    public decimal? SpotMoving7DayQuoteVolumeUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_30_DAY_QUOTE_VOLUME_USD")]
    public decimal? SpotMoving30DayQuoteVolumeUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_24_HOUR_CHANGE_PERCENTAGE_USD")]
    public decimal? SpotMoving24HourChangePercentageUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_7_DAY_CHANGE_PERCENTAGE_USD")]
    public decimal? SpotMoving7DayChangePercentageUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_30_DAY_CHANGE_PERCENTAGE_USD")]
    public decimal? SpotMoving30DayChangePercentageUsd { get; set; }

    [JsonPropertyName("TOPLIST_BASE_RANK")]
    public CoinDeskToplistRank? ToplistBaseRank { get; set; }

    [JsonPropertyName("ASSET_INDUSTRIES")]
    public List<CoinDeskIndustry>? AssetIndustries { get; set; }
}

/// <summary>
/// CoinDesk 其他平台 ID
/// </summary>
public class CoinDeskAlternativeId
{
    [JsonPropertyName("NAME")]
    public string? Name { get; set; }

    [JsonPropertyName("ID")]
    public string? Id { get; set; }
}

/// <summary>
/// CoinDesk 安全审计指标
/// </summary>
public class CoinDeskSecurityMetric
{
    [JsonPropertyName("NAME")]
    public string? Name { get; set; }

    [JsonPropertyName("OVERALL_SCORE")]
    public decimal? OverallScore { get; set; }

    [JsonPropertyName("OVERALL_RANK")]
    public int? OverallRank { get; set; }
}

/// <summary>
/// CoinDesk 排名信息
/// </summary>
public class CoinDeskToplistRank
{
    [JsonPropertyName("CIRCULATING_MKT_CAP_USD")]
    public int? CirculatingMktCapUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_24_HOUR_QUOTE_VOLUME_USD")]
    public int? SpotMoving24HourQuoteVolumeUsd { get; set; }

    [JsonPropertyName("SPOT_MOVING_30_DAY_QUOTE_VOLUME_USD")]
    public int? SpotMoving30DayQuoteVolumeUsd { get; set; }
}

/// <summary>
/// CoinDesk 行业分类
/// </summary>
public class CoinDeskIndustry
{
    [JsonPropertyName("ASSET_INDUSTRY")]
    public string? AssetIndustry { get; set; }
}
