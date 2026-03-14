using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币基础数据工具实现（使用服务层获取数据）
/// </summary>
public sealed class CryptoBasicTools : ICryptoBasicTools
{
    private readonly ILogger<CryptoBasicTools> _logger;
    private readonly BinanceMarketDataService _binanceService;
    private readonly CoinDeskApiService _coinDeskService;
    private readonly IUserSettingService _userSettingService;

    public CryptoBasicTools(
        ILogger<CryptoBasicTools> logger,
        BinanceMarketDataService binanceService,
        CoinDeskApiService coinDeskService,
        IUserSettingService userSettingService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
        _coinDeskService = coinDeskService ?? throw new ArgumentNullException(nameof(coinDeskService));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    }

    /// <summary>
    /// 根据资产代码获取基本数据（使用币安 API 获取 24 小时行情统计）
    /// </summary>
    /// <param name="assetSymbol">虚拟币代码（如 BTC、ETH、BNB）</param>
    /// <returns>包含价格、涨跌幅、成交量等行情信息的虚拟币报价数据</returns>
    [Description("根据虚拟币代码获取基本数据，包括实时行情、价格变动、成交量等信息。symbol格式不区分大小写，如BTC或BTCUSDT。")]
    public async Task<CryptoQuoteInfo> GetAssetInfoAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            // 格式化交易对符号（如 "BTC" -> "BTCUSDT"）
            var symbol = ToBinanceFormat(assetSymbol);

            _logger.LogInformation("正在获取虚拟币行情数据: {Symbol}", symbol);

            var tickerData = await _binanceService.Get24hrTickerAsync(symbol);

            if (tickerData == null)
            {
                _logger.LogError("币安API返回数据为空: {Symbol}", assetSymbol);
                throw new FriendlyException($"无法获取虚拟币行情: {assetSymbol}");
            }

            // 映射到 CryptoQuoteInfo 模型
            var quoteInfo = new CryptoQuoteInfo
            {
                SecurityCode = assetSymbol.ToUpper(),
                SecurityName = assetSymbol.ToUpper(),
                SecurityType = "虚拟币",
                TradeStatus = "交易中",

                // 价格信息
                CurrentPrice = tickerData.LastPrice,
                OpenPrice = tickerData.OpenPrice,
                HighPrice = tickerData.HighPrice,
                LowPrice = tickerData.LowPrice,
                PreviousClosePrice = tickerData.PrevClosePrice ?? tickerData.OpenPrice,
                AveragePrice = tickerData.WeightedAvgPrice ?? tickerData.LastPrice,

                // 涨跌信息
                PriceChange = tickerData.PriceChange ?? 0m,
                PercentageChange = tickerData.PriceChangePercent ?? 0m,
                Amplitude = CalculateAmplitude(tickerData.HighPrice, tickerData.LowPrice, tickerData.PrevClosePrice ?? tickerData.OpenPrice),

                // 交易量（币安原始单位）
                Volume = tickerData.Volume, // 24h成交量（币数量）
                Amount = tickerData.QuoteVolume // 24h成交额（USDT）
            };

            _logger.LogInformation("成功获取虚拟币行情: {Symbol}, 当前价: {Price}, 涨跌幅: {Change}%",
                assetSymbol, quoteInfo.CurrentPrice, quoteInfo.PercentageChange);

            return quoteInfo;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用币安API获取行情失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取虚拟币行情失败: {assetSymbol}，请检查网络连接或交易对是否正确", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取虚拟币行情时发生错误: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取虚拟币行情时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 计算振幅（当日最高价与最低价的差值占前收盘价的百分比）
    /// </summary>
    /// <param name="high">当日最高价</param>
    /// <param name="low">当日最低价</param>
    /// <param name="prevClose">前一日收盘价</param>
    /// <returns>振幅百分比（如 5.23 表示 5.23%）</returns>
    private static decimal CalculateAmplitude(decimal high, decimal low, decimal prevClose)
    {
        if (prevClose == 0) return 0;
        return Math.Round(((high - low) / prevClose) * 100, 2);
    }

    /// <summary>
    /// 根据虚拟币代码获取区块链项目基本面信息（使用 CoinDesk API）
    /// </summary>
    /// <param name="assetSymbol">虚拟币代码（如 BTC、ETH、BNB）</param>
    /// <returns>包含项目描述、供应量、价格、市值、交易量等完整基本面信息</returns>
    [Description("根据虚拟币代码获取区块链项目基本面信息，包括项目简介、社区数据、开发者活跃度等。symbol格式不区分大小写，如BTC。")]
    public async Task<CryptoProjectInfo> GetProjectInfoAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            // 提取基础币种 (如 BTCUSDT -> BTC)
            var baseSymbol = CryptoSymbolConverter.ExtractBaseCurrency(assetSymbol);
            var symbol = string.IsNullOrWhiteSpace(baseSymbol) ? assetSymbol.ToUpper() : baseSymbol;

            _logger.LogInformation("正在获取虚拟币项目信息: {Symbol}", symbol);

            var coinDeskResponse = await _coinDeskService.GetAssetMetadataAsync(symbol);

            if (coinDeskResponse?.Data == null || !coinDeskResponse.Data.ContainsKey(symbol))
            {
                _logger.LogError("CoinDesk API 返回数据为空: {Symbol}", symbol);
                throw new FriendlyException($"无法获取虚拟币项目信息: {symbol}，可能是非主流币种或名称错误");
            }

            var assetData = coinDeskResponse.Data[symbol];

            // 映射到 CryptoProjectInfo 模型
            var projectInfo = new CryptoProjectInfo
            {
                Symbol = assetData.Symbol ?? symbol,
                Name = assetData.Name ?? symbol,
                Uri = assetData.Uri ?? "",
                AssetType = assetData.AssetType ?? "",
                AlternativeIds = ExtractAlternativeIds(assetData.AssetAlternativeIds),
                DescriptionSnippet = assetData.AssetDescriptionSnippet ?? "",
                Description = assetData.AssetDescription ?? "",
                SecurityMetrics = ExtractSecurityMetrics(assetData.AssetSecurityMetrics),
                MaxSupply = assetData.SupplyMax,
                TotalSupply = assetData.SupplyTotal,
                CirculatingSupply = assetData.SupplyCirculating,
                PriceUsd = assetData.PriceUsd,
                TotalMarketCapUsd = assetData.TotalMktCapUsd,
                CirculatingMarketCapUsd = assetData.CirculatingMktCapUsd,
                Volume24hUsd = assetData.SpotMoving24HourQuoteVolumeUsd,
                Volume7dUsd = assetData.SpotMoving7DayQuoteVolumeUsd,
                Volume30dUsd = assetData.SpotMoving30DayQuoteVolumeUsd,
                Change24hPercent = assetData.SpotMoving24HourChangePercentageUsd,
                Change7dPercent = assetData.SpotMoving7DayChangePercentageUsd,
                Change30dPercent = assetData.SpotMoving30DayChangePercentageUsd,
                Rankings = ExtractRankings(assetData.ToplistBaseRank),
                Industries = ExtractIndustries(assetData.AssetIndustries)
            };

            _logger.LogInformation("成功获取虚拟币项目信息: {Name} ({Symbol})", projectInfo.Name, symbol);

            return projectInfo;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 CoinDesk API 获取项目信息失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取虚拟币项目信息失败: {assetSymbol}，请检查网络连接", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取虚拟币项目信息时发生错误: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取虚拟币项目信息时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 提取其他平台 ID（CoinMarketCap、CoinGecko 等）
    /// </summary>
    private static Dictionary<string, string> ExtractAlternativeIds(List<CoinDeskAlternativeId>? alternativeIds)
    {
        var result = new Dictionary<string, string>();
        if (alternativeIds == null) return result;

        foreach (var altId in alternativeIds)
        {
            if (!string.IsNullOrEmpty(altId.Name) && !string.IsNullOrEmpty(altId.Id))
            {
                result[altId.Name] = altId.Id;
            }
        }
        return result;
    }

    /// <summary>
    /// 提取安全审计指标（CertiK 等）
    /// </summary>
    private static SecurityMetrics? ExtractSecurityMetrics(List<CoinDeskSecurityMetric>? metrics)
    {
        if (metrics == null) return null;

        var certik = metrics.FirstOrDefault(m => m.Name?.Equals("CERTIK", StringComparison.OrdinalIgnoreCase) == true);
        if (certik == null) return null;

        return new SecurityMetrics
        {
            CertikScore = certik.OverallScore,
            CertikRank = certik.OverallRank
        };
    }

    /// <summary>
    /// 提取排名信息
    /// </summary>
    private static RankingInfo? ExtractRankings(CoinDeskToplistRank? rankings)
    {
        if (rankings == null) return null;

        return new RankingInfo
        {
            MarketCapRank = rankings.CirculatingMktCapUsd,
            Volume24hRank = rankings.SpotMoving24HourQuoteVolumeUsd,
            Volume30dRank = rankings.SpotMoving30DayQuoteVolumeUsd
        };
    }

    /// <summary>
    /// 提取行业分类信息
    /// </summary>
    private static List<string> ExtractIndustries(List<CoinDeskIndustry>? industries)
    {
        if (industries == null) return new List<string>();

        return industries
            .Where(i => !string.IsNullOrEmpty(i.AssetIndustry))
            .Select(i => i.AssetIndustry!)
            .ToList();
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetAssetInfoAsync);
        yield return AIFunctionFactory.Create(GetProjectInfoAsync);
    }
}