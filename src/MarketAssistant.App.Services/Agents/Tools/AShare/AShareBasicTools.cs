using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

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
    public async Task<StockQuoteInfo> GetAssetInfoAsync([Description("股票代码")] string assetSymbol)
    {
        try
        {
            var formattedSymbol = StockSymbolConverter.ToClsFormat(assetSymbol);
            if (string.IsNullOrEmpty(formattedSymbol))
                throw new FriendlyException($"股票代码格式不正确: {assetSymbol}");

            var url = $"/quote/stock/basic?secu_code={formattedSymbol}&fields=open_px,av_px,high_px,low_px,change,change_px,down_price,change_3,change_5,qrr,entrust_rate,tr,amp,TotalShares,mc,NetAssetPS,NonRestrictedShares,cmc,business_amount,business_balance,pe,ttm_pe,pb,secu_name,secu_code,trade_status,secu_type,preclose_px,up_price,last_px&app=CailianpressWeb&os=web&sv=8.4.6";

            using var httpClient = _httpClientFactory.CreateClient("Cls");
            var response = await httpClient.GetStringAsync(url);
            var jsonDocument = JsonDocument.Parse(response);

            if (jsonDocument.RootElement.TryGetProperty("data", out var data) == false || data.ValueKind == JsonValueKind.Null)
                throw new FriendlyException($"未找到股票 {assetSymbol} ({formattedSymbol}) 的数据，请检查代码是否正确。");

            var stockPriceInfo = new StockQuoteInfo();

            stockPriceInfo.CurrentPrice = data.GetProperty("last_px").GetDecimal();
            stockPriceInfo.PriceChange = data.GetProperty("change_px").GetDecimal();
            stockPriceInfo.PercentageChange = data.GetProperty("change").GetDecimal();
            stockPriceInfo.HighPrice = data.GetProperty("high_px").GetDecimal();
            stockPriceInfo.LowPrice = data.GetProperty("low_px").GetDecimal();
            stockPriceInfo.Volume = data.GetProperty("business_amount").GetDecimal() / 10000;
            stockPriceInfo.Amount = data.GetProperty("business_balance").GetDecimal() / 100000000;
            stockPriceInfo.TurnoverRate = data.GetProperty("tr").GetDecimal();
            stockPriceInfo.PercentageChange3Day = data.GetProperty("change_3").GetDecimal();
            stockPriceInfo.PercentageChange5Day = data.GetProperty("change_5").GetDecimal();
            stockPriceInfo.TotalShares = data.GetProperty("TotalShares").GetDecimal();
            stockPriceInfo.MarketCapitalization = data.GetProperty("mc").GetDecimal() / 100000000;
            stockPriceInfo.SecurityName = data.GetProperty("secu_name").GetString() ?? string.Empty;
            stockPriceInfo.SecurityCode = data.GetProperty("secu_code").GetString() ?? string.Empty;
            stockPriceInfo.TradeStatus = data.GetProperty("trade_status").GetString() ?? string.Empty;
            stockPriceInfo.SecurityType = data.GetProperty("secu_type").GetString() ?? string.Empty;
            stockPriceInfo.OpenPrice = data.GetProperty("open_px").GetDecimal();
            stockPriceInfo.PreviousClosePrice = data.GetProperty("preclose_px").GetDecimal();
            stockPriceInfo.UpLimitPrice = data.GetProperty("up_price").GetDecimal();
            stockPriceInfo.DownLimitPrice = data.GetProperty("down_price").GetDecimal();
            stockPriceInfo.Amplitude = data.GetProperty("amp").GetDecimal();
            stockPriceInfo.PERatio = data.GetProperty("pe").GetDecimal();
            stockPriceInfo.TTMPERatio = data.GetProperty("ttm_pe").GetDecimal();
            stockPriceInfo.PBRatio = data.GetProperty("pb").GetDecimal();
            stockPriceInfo.CirculationMarketCap = data.GetProperty("cmc").GetDecimal() / 100000000;
            stockPriceInfo.NonRestrictedShares = data.GetProperty("NonRestrictedShares").GetDecimal();
            stockPriceInfo.NetAssetPerShare = data.GetProperty("NetAssetPS").GetDecimal();
            stockPriceInfo.AveragePrice = data.GetProperty("av_px").GetDecimal();
            stockPriceInfo.VolumeRatio = data.GetProperty("qrr").GetDecimal();
            stockPriceInfo.EntrustRatio = data.GetProperty("entrust_rate").GetDecimal();

            return stockPriceInfo;
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取股票行情失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"处理股票价格数据时发生错误: {ex.Message}", ex);
        }
    }

    [Description("根据股票代码获取上市公司基本面信息，包括公司简介、主营业务、所属行业等")]
    public async Task<CompanyInfo> GetCompanyInfoAsync([Description("股票代码")] string assetSymbol)
    {
        try
        {
            assetSymbol = new string(assetSymbol.Where(char.IsDigit).ToArray());

            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var url = $"/hs/gs/gsjj/{assetSymbol}?token={token}";

            using var httpClient = _httpClientFactory.CreateClient("ZhiTu");
            var response = await httpClient.GetStringAsync(url);
            var info = JsonSerializer.Deserialize<CompanyInfo>(response);

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
}
