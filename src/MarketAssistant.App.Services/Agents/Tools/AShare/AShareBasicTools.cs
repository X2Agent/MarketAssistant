using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股基础数据工具实现
/// </summary>
public sealed class AShareBasicTools : IShareBasicTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<AShareBasicTools> _logger;

    public AShareBasicTools(
        IHttpClientFactory httpClientFactory,
        IUserSettingService userSettingService,
        ILogger<AShareBasicTools> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _logger = logger;
    }

    [Description("根据股票代码获取股票基本数据，包括实时行情、价格变动、市值等信息")]
    public async Task<StockQuoteInfo> GetAssetInfoAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var formattedSymbol = StockSymbolConverter.ToClsFormat(assetSymbol);
            if (string.IsNullOrEmpty(formattedSymbol))
                throw new FriendlyException($"股票代码格式不正确: {assetSymbol}");

            var url = $"/quote/stock/basic?secu_code={formattedSymbol}&fields=open_px,av_px,high_px,low_px,change,change_px,down_price,change_3,change_5,qrr,entrust_rate,tr,amp,TotalShares,mc,NetAssetPS,NonRestrictedShares,cmc,business_amount,business_balance,pe,ttm_pe,pb,secu_name,secu_code,trade_status,secu_type,preclose_px,up_price,last_px&app=CailianpressWeb&os=web&sv=8.4.6";

            using var httpClient = _httpClientFactory.CreateClient("Cls");
            var response = await httpClient.GetStringAsync(url, cancellationToken);
            using var jsonDocument = JsonDocument.Parse(response);

            if (jsonDocument.RootElement.TryGetProperty("data", out var data) == false || data.ValueKind == JsonValueKind.Null)
                throw new FriendlyException($"未找到股票 {assetSymbol} ({formattedSymbol}) 的数据，请检查代码是否正确。");

            // 通过 StringToDecimalConverter 容错反序列化：字符串数值/null/--占位均安全降级，不再抛出转换异常
            var raw = JsonSerializer.Deserialize<ClsStockQuoteData>(data.GetRawText(), JsonOptions.AShareApiOptions)
                ?? throw new FriendlyException($"解析股票 {assetSymbol} 行情数据失败。");

            return new StockQuoteInfo
            {
                CurrentPrice = raw.LastPrice,
                PriceChange = raw.ChangePx,
                PercentageChange = raw.Change,
                HighPrice = raw.HighPx,
                LowPrice = raw.LowPx,
                Volume = raw.BusinessAmount / 10000m,
                Amount = raw.BusinessBalance / 100000000m,
                TurnoverRate = raw.TurnoverRate,
                PercentageChange3Day = raw.Change3,
                PercentageChange5Day = raw.Change5,
                TotalShares = raw.TotalShares,
                MarketCapitalization = raw.MarketCap / 100000000m,
                SecurityName = raw.SecurityName ?? string.Empty,
                SecurityCode = raw.SecurityCode ?? string.Empty,
                TradeStatus = raw.TradeStatus ?? string.Empty,
                SecurityType = raw.SecurityType ?? string.Empty,
                OpenPrice = raw.OpenPx,
                PreviousClosePrice = raw.PreClosePx,
                UpLimitPrice = raw.UpPrice,
                DownLimitPrice = raw.DownPrice,
                Amplitude = raw.Amplitude,
                PERatio = raw.PERatio,
                TTMPERatio = raw.TTMPERatio,
                PBRatio = raw.PBRatio,
                CirculationMarketCap = raw.CirculationMarketCap / 100000000m,
                NonRestrictedShares = raw.NonRestrictedShares,
                NetAssetPerShare = raw.NetAssetPS,
                AveragePrice = raw.AveragePx,
                VolumeRatio = raw.VolumeRatio,
                EntrustRatio = raw.EntrustRate
            };
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取股票行情失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"处理股票价格数据时发生错误: {ex.Message}", ex);
        }
    }

    [Description("根据股票代码获取上市公司基本面信息，包括公司简介、主营业务、所属行业等")]
    public async Task<CompanyInfo> GetCompanyInfoAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
    {
        try
        {
            assetSymbol = new string(assetSymbol.Where(char.IsDigit).ToArray());

            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var url = $"/hs/gs/gsjj/{assetSymbol}?token={token}";

            using var httpClient = _httpClientFactory.CreateClient("ZhiTu");
            var response = await httpClient.GetStringAsync(url, cancellationToken);
            var info = JsonSerializer.Deserialize<CompanyInfo>(response, JsonOptions.AShareApiOptions);

            return info ?? throw new FriendlyException("GetCompanyInfoAsync返回数据为空");
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取公司基本面失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"处理公司基本面数据时发生错误: {ex.Message}", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetAssetInfoAsync);
        yield return AIFunctionFactory.Create(GetCompanyInfoAsync);
    }

    /// <summary>
    /// 财联社（cls.cn）行情接口返回字段映射，统一以 decimal 接收数值，
    /// 配合 StringToDecimalConverter 容错字符串/null/--占位。
    /// </summary>
    private sealed class ClsStockQuoteData
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
}
