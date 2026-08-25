using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 财联社（cls.cn）行情接口 <c>/quote/stock/basic</c> 返回字段映射。
/// 统一以 decimal 接收数值，配合 AShareJsonOptions 容错字符串/null/-- 占位。
/// </summary>
public sealed class ClsStockQuoteData
{
    [JsonPropertyName("last_px")] public decimal LastPrice { get; set; }
    [JsonPropertyName("change_px")] public decimal ChangePx { get; set; }
    [JsonPropertyName("change")] public decimal Change { get; set; }
    [JsonPropertyName("high_px")] public decimal HighPx { get; set; }
    [JsonPropertyName("low_px")] public decimal LowPx { get; set; }
    [JsonPropertyName("business_amount")] public decimal BusinessAmount { get; set; }
    [JsonPropertyName("business_balance")] public decimal BusinessBalance { get; set; }
    [JsonPropertyName("tr")] public decimal TurnoverRate { get; set; }
    [JsonPropertyName("change_3")] public decimal Change3 { get; set; }
    [JsonPropertyName("change_5")] public decimal Change5 { get; set; }
    [JsonPropertyName("TotalShares")] public decimal TotalShares { get; set; }
    [JsonPropertyName("mc")] public decimal MarketCap { get; set; }
    [JsonPropertyName("cmc")] public decimal CirculationMarketCap { get; set; }
    [JsonPropertyName("NonRestrictedShares")] public decimal NonRestrictedShares { get; set; }
    [JsonPropertyName("NetAssetPS")] public decimal NetAssetPS { get; set; }
    [JsonPropertyName("open_px")] public decimal OpenPx { get; set; }
    [JsonPropertyName("preclose_px")] public decimal PreClosePx { get; set; }
    [JsonPropertyName("up_price")] public decimal UpPrice { get; set; }
    [JsonPropertyName("down_price")] public decimal DownPrice { get; set; }
    [JsonPropertyName("amp")] public decimal Amplitude { get; set; }
    [JsonPropertyName("pe")] public decimal PERatio { get; set; }
    [JsonPropertyName("ttm_pe")] public decimal TTMPERatio { get; set; }
    [JsonPropertyName("pb")] public decimal PBRatio { get; set; }
    [JsonPropertyName("av_px")] public decimal AveragePx { get; set; }
    [JsonPropertyName("qrr")] public decimal VolumeRatio { get; set; }
    [JsonPropertyName("entrust_rate")] public decimal EntrustRate { get; set; }
    [JsonPropertyName("secu_name")] public string? SecurityName { get; set; }
    [JsonPropertyName("secu_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("trade_status")] public string? TradeStatus { get; set; }
    [JsonPropertyName("secu_type")] public string? SecurityType { get; set; }
}

/// <summary>
/// 财联社股票搜索结果条目（<c>/api/sw</c>）。
/// </summary>
public sealed record ClsStockSearchItem(string Name, string StockId);