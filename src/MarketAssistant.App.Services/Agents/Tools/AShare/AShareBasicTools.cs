using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股基础数据工具实现
/// </summary>
public sealed class AShareBasicTools : IBasicDataTools
{
    /// <summary>CLS 行情接口请求字段列表。</summary>
    private const string QuoteFields =
        "open_px,av_px,high_px,low_px,change,change_px,down_price,change_3,change_5,qrr,entrust_rate,tr,amp," +
        "TotalShares,mc,NetAssetPS,NonRestrictedShares,cmc,business_amount,business_balance,pe,ttm_pe,pb," +
        "secu_name,secu_code,trade_status,secu_type,preclose_px,up_price,last_px";

    private readonly ClsQuoteClient _clsClient;
    private readonly ZhiTuMarketClient _zhiTuClient;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<AShareBasicTools> _logger;

    public AShareBasicTools(
        ClsQuoteClient clsClient,
        ZhiTuMarketClient zhiTuClient,
        IUserSettingService userSettingService,
        ILogger<AShareBasicTools> logger)
    {
        _clsClient = clsClient ?? throw new ArgumentNullException(nameof(clsClient));
        _zhiTuClient = zhiTuClient ?? throw new ArgumentNullException(nameof(zhiTuClient));
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

            // HTTP 访问与容错反序列化由 ClsQuoteClient 负责
            var raw = await _clsClient.GetStockQuoteAsync(formattedSymbol, QuoteFields, cancellationToken)
                ?? throw new FriendlyException($"未找到股票 {assetSymbol} ({formattedSymbol}) 的数据，请检查代码是否正确。");
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
            var info = await _zhiTuClient.GetCompanyInfoAsync<CompanyInfo>(assetSymbol, token, cancellationToken);

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