using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Services.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币市场指标工具实现
/// </summary>
/// <remarks>
/// 职责：提供市场深度数据、综合市场指标和衍生计算指标
/// 数据源：币安（市场深度）、CoinGecko（市场指标、交易量分布）
/// </remarks>
public sealed class CryptoMetricsTools : ICryptoMetricsTools
{
    private readonly BinanceMarketDataService _binanceService;
    private readonly CoinGeckoApiService _coinGeckoService;
    private readonly ILogger<CryptoMetricsTools> _logger;

    public CryptoMetricsTools(
        BinanceMarketDataService binanceService,
        CoinGeckoApiService coinGeckoService,
        ILogger<CryptoMetricsTools> logger)
    {
        _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
        _coinGeckoService = coinGeckoService ?? throw new ArgumentNullException(nameof(coinGeckoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ==================== 市场深度数据（币安） ====================

    /// <summary>
    /// 获取历史K线数据（OHLCV）
    /// </summary>
    [Description("获取历史K线数据（开盘、最高、最低、收盘、成交量），用于技术分析。interval为枚举类型，支持如 OneDay, OneHour 等")]
    public async Task<CryptoOHLCV> GetOHLCVAsync(string symbol, MarketInterval interval = MarketInterval.OneDay, int limit = 500, long? startTime = null, long? endTime = null)
    {
        var intervalStr = ToBinanceInterval(interval);
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);

            var response = await _binanceService.GetKlinesAsync(binanceSymbol, intervalStr, limit, startTime, endTime);
            if (response == null || response.Count == 0)
            {
                throw new FriendlyException($"未找到交易对 {binanceSymbol} 的K线数据，请确认代码是否正确");
            }

            var candles = new List<OHLCVCandle>();
            foreach (var item in response)
            {
                if (item is not JsonArray arr || arr.Count < 11) continue;

                candles.Add(new OHLCVCandle
                {
                    OpenTime = arr[0]?.GetValue<long>() ?? 0,
                    Open = decimal.Parse(arr[1]?.GetValue<string>() ?? "0"),
                    High = decimal.Parse(arr[2]?.GetValue<string>() ?? "0"),
                    Low = decimal.Parse(arr[3]?.GetValue<string>() ?? "0"),
                    Close = decimal.Parse(arr[4]?.GetValue<string>() ?? "0"),
                    Volume = decimal.Parse(arr[5]?.GetValue<string>() ?? "0"),
                    CloseTime = arr[6]?.GetValue<long>() ?? 0,
                    QuoteVolume = decimal.Parse(arr[7]?.GetValue<string>() ?? "0"),
                    TradeCount = arr[8]?.GetValue<int>() ?? 0
                });
            }

            return new CryptoOHLCV
            {
                Symbol = binanceSymbol,
                Interval = intervalStr,
                Candles = candles
            };
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取K线数据失败: {Symbol}, Interval: {Interval}", symbol, intervalStr);
            throw new FriendlyException($"获取K线数据失败: {ex.Message}", ex);
        }
    }

    private static string ToBinanceInterval(MarketInterval interval)
    {
        return interval switch
        {
            MarketInterval.OneSecond => "1s",
            MarketInterval.OneMinute => "1m",
            MarketInterval.ThreeMinutes => "3m",
            MarketInterval.FiveMinutes => "5m",
            MarketInterval.FifteenMinutes => "15m",
            MarketInterval.ThirtyMinutes => "30m",
            MarketInterval.OneHour => "1h",
            MarketInterval.TwoHours => "2h",
            MarketInterval.FourHours => "4h",
            MarketInterval.SixHours => "6h",
            MarketInterval.EightHours => "8h",
            MarketInterval.TwelveHours => "12h",
            MarketInterval.OneDay => "1d",
            MarketInterval.ThreeDays => "3d",
            MarketInterval.OneWeek => "1w",
            MarketInterval.OneMonth => "1M",
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
        };
    }

    /// <summary>
    /// 获取订单簿深度数据
    /// </summary>
    [Description("获取订单簿深度数据，包括买卖盘挂单，用于分析支撑压力位。limit通常为20/50/100")]
    public async Task<CryptoOrderBookDepth> GetOrderBookDepthAsync(string symbol, int limit = 100)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);

            var response = await _binanceService.GetDepthAsync(binanceSymbol, limit);
            if (response == null)
            {
                throw new FriendlyException($"未找到交易对 {binanceSymbol} 的深度数据，请确认代码是否正确。");
            }

            var bids = new List<OrderBookLevel>();
            var asks = new List<OrderBookLevel>();

            if (response["bids"] is JsonArray bidsArray)
            {
                foreach (var bid in bidsArray)
                {
                    if (bid is JsonArray bidArr && bidArr.Count >= 2)
                    {
                        bids.Add(new OrderBookLevel
                        {
                            Price = decimal.Parse(bidArr[0]?.GetValue<string>() ?? "0"),
                            Quantity = decimal.Parse(bidArr[1]?.GetValue<string>() ?? "0")
                        });
                    }
                }
            }

            if (response["asks"] is JsonArray asksArray)
            {
                foreach (var ask in asksArray)
                {
                    if (ask is JsonArray askArr && askArr.Count >= 2)
                    {
                        asks.Add(new OrderBookLevel
                        {
                            Price = decimal.Parse(askArr[0]?.GetValue<string>() ?? "0"),
                            Quantity = decimal.Parse(askArr[1]?.GetValue<string>() ?? "0")
                        });
                    }
                }
            }

            return new CryptoOrderBookDepth
            {
                Symbol = binanceSymbol,
                LastUpdateId = response["lastUpdateId"]?.GetValue<long>() ?? 0,
                Bids = bids,
                Asks = asks
            };
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取订单簿深度失败: {Symbol}", symbol);
            throw new FriendlyException($"获取订单簿深度失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取最近成交数据
    /// </summary>
    [Description("获取最近成交记录，分析主动买入/卖出力量对比")]
    public async Task<CryptoRecentTrades> GetRecentTradesAsync(string symbol, int limit = 500)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);

            var response = await _binanceService.GetRecentTradesAsync(binanceSymbol, limit);
            if (response == null || response.Count == 0)
            {
                throw new FriendlyException($"未找到交易对 {binanceSymbol} 的成交数据，请确认代码是否正确。");
            }

            var trades = new List<CryptoTrade>();
            decimal buyVolume = 0;
            decimal sellVolume = 0;

            foreach (var item in response)
            {
                if (item is not JsonObject obj) continue;

                var price = decimal.Parse(obj["price"]?.GetValue<string>() ?? "0");
                var qty = decimal.Parse(obj["qty"]?.GetValue<string>() ?? "0");
                var isBuyerMaker = obj["isBuyerMaker"]?.GetValue<bool>() ?? false;

                trades.Add(new CryptoTrade
                {
                    TradeId = obj["id"]?.GetValue<long>() ?? 0,
                    Price = price,
                    Quantity = qty,
                    QuoteQuantity = obj["quoteQty"] != null ? decimal.Parse(obj["quoteQty"].GetValue<string>()) : price * qty,
                    Timestamp = obj["time"]?.GetValue<long>() ?? 0,
                    IsBuyerMaker = isBuyerMaker
                });

                if (isBuyerMaker)
                    sellVolume += qty;  // Buyer是maker表示主动卖出
                else
                    buyVolume += qty;   // Buyer是taker表示主动买入
            }

            var totalVolume = buyVolume + sellVolume;

            return new CryptoRecentTrades
            {
                Symbol = binanceSymbol,
                Trades = trades,
                BuyerVolumePercent = totalVolume > 0 ? buyVolume / totalVolume * 100 : 0,
                SellerVolumePercent = totalVolume > 0 ? sellVolume / totalVolume * 100 : 0
            };
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取最近成交失败: {Symbol}", symbol);
            throw new FriendlyException($"获取最近成交失败: {ex.Message}", ex);
        }
    }

    // ==================== 综合市场指标（CoinGecko） ====================

    /// <summary>
    /// 获取综合市场指标
    /// </summary>
    [Description("获取CoinGecko全方位市场数据：市值、FDV、ATH/ATL、历史排名等")]
    public async Task<CryptoMarketMetrics> GetMarketMetricsAsync(string symbol)
    {
        try
        {
            var coinId = await GetCoinGeckoIdAsync(symbol);

            var response = await _coinGeckoService.GetCoinMarketDataAsync(coinId, "usd", "24h,7d,30d");
            if (response == null || response.Count == 0)
            {
                throw new FriendlyException($"未找到代币 {symbol} 的市场数据，可能是CoinGecko ID不匹配。");
            }

            var data = response[0] as JsonObject;
            if (data == null)
            {
                throw new FriendlyException($"解析代币 {symbol} 的市场数据失败");
            }

            return new CryptoMarketMetrics
            {
                Symbol = symbol.ToUpperInvariant(),
                CurrentPriceUsd = data["current_price"]?.GetValue<decimal>() ?? 0,
                MarketCapUsd = data["market_cap"]?.GetValue<decimal>() ?? 0,
                FullyDilutedValuationUsd = data["fully_diluted_valuation"]?.GetValue<decimal?>(),
                CirculatingSupply = data["circulating_supply"]?.GetValue<decimal>() ?? 0,
                TotalSupply = data["total_supply"]?.GetValue<decimal?>(),
                MaxSupply = data["max_supply"]?.GetValue<decimal?>(),
                Volume24hUsd = data["total_volume"]?.GetValue<decimal>() ?? 0,
                PriceChange24hPercent = data["price_change_percentage_24h"]?.GetValue<decimal>() ?? 0,
                PriceChange7dPercent = data["price_change_percentage_7d_in_currency"]?.GetValue<decimal?>(),
                PriceChange30dPercent = data["price_change_percentage_30d_in_currency"]?.GetValue<decimal?>(),
                MarketCapRank = data["market_cap_rank"]?.GetValue<int?>(),
                AllTimeHighUsd = data["ath"]?.GetValue<decimal?>(),
                AthChangePercent = data["ath_change_percentage"]?.GetValue<decimal?>(),
                AllTimeLowUsd = data["atl"]?.GetValue<decimal?>(),
                AtlChangePercent = data["atl_change_percentage"]?.GetValue<decimal?>(),
                LastUpdated = data["last_updated"]?.GetValue<DateTime>() ?? DateTime.UtcNow
            };
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取市场指标失败: {Symbol}", symbol);
            throw new FriendlyException($"获取市场指标失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取交易量分布
    /// </summary>
    [Description("获取代币在不同交易所的交易量分布情况，用于分析流动性分布")]
    public async Task<List<VolumeDistribution>> GetVolumeDistributionAsync(string symbol)
    {
        try
        {
            var coinId = await GetCoinGeckoIdAsync(symbol);

            var response = await _coinGeckoService.GetCoinTickersAsync(coinId);
            if (response?.Tickers == null || response.Tickers.Count == 0)
            {
                return [];
            }

            // 聚合各交易所的交易量
            var exchangeVolumes = response.Tickers
                .GroupBy(t => t.Market?.Name ?? "Unknown")
                .Select(g => new
                {
                    Exchange = g.Key,
                    Volume = g.Sum(t => t.ConvertedVolume?.Usd ?? 0)
                })
                .OrderByDescending(x => x.Volume)
                .ToList();

            var totalVolume = exchangeVolumes.Sum(x => x.Volume);
            if (totalVolume == 0) return [];

            return exchangeVolumes
                .Select(x => new VolumeDistribution
                {
                    Exchange = x.Exchange,
                    Volume = (decimal)x.Volume,
                    Percentage = (decimal)(x.Volume / totalVolume * 100)
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取交易量分布失败: {Symbol}", symbol);
            throw new FriendlyException($"获取交易量分布失败: {ex.Message}", ex);
        }
    }
    /// <summary>
    /// 获取波动性指标
    /// </summary>
    [Description("获取波动性指标，包括历史波动率、ATR、最大回撤、夏普比率等，用于风险评估")]
    public async Task<CryptoVolatilityMetrics> GetVolatilityMetricsAsync(string symbol, int days = 30)
    {
        try
        {
            // 获取历史K线数据
            var ohlcv = await GetOHLCVAsync(symbol, MarketInterval.OneDay, days + 1);
            if (ohlcv.Candles.Count < 2)
            {
                throw new FriendlyException($"数据不足，无法计算波动性指标");
            }

            var candles = ohlcv.Candles;
            var returns = new List<decimal>();
            var trueRanges = new List<decimal>();

            // 计算日收益率和真实波幅
            for (int i = 1; i < candles.Count; i++)
            {
                var prevClose = candles[i - 1].Close;
                var currentClose = candles[i].Close;
                var currentHigh = candles[i].High;
                var currentLow = candles[i].Low;

                // 日收益率
                if (prevClose > 0)
                {
                    returns.Add((currentClose - prevClose) / prevClose);
                }

                // 真实波幅 = max(H-L, |H-PC|, |L-PC|)
                var tr1 = currentHigh - currentLow;
                var tr2 = Math.Abs(currentHigh - prevClose);
                var tr3 = Math.Abs(currentLow - prevClose);
                trueRanges.Add(Math.Max(tr1, Math.Max(tr2, tr3)));
            }

            // 计算统计指标
            var avgReturn = returns.Average();
            var variance = returns.Sum(r => (r - avgReturn) * (r - avgReturn)) / (returns.Count - 1);
            var stdDev = (decimal)Math.Sqrt((double)variance);
            var dailyVol = stdDev * 100;  // 转换为百分比
            var annualizedVol = dailyVol * (decimal)Math.Sqrt(365);  // 年化（虚拟币市场7x24交易，365天）
            var atr = trueRanges.Average();

            // 计算最大回撤
            decimal maxDrawdown = 0;
            long maxDDStart = 0;
            long maxDDEnd = 0;
            decimal peak = candles[0].Close;
            long peakTime = candles[0].OpenTime;

            for (int i = 0; i < candles.Count; i++)
            {
                var price = candles[i].Close;
                if (price > peak)
                {
                    peak = price;
                    peakTime = candles[i].OpenTime;
                }
                else
                {
                    var drawdown = (peak - price) / peak * 100;
                    if (drawdown > maxDrawdown)
                    {
                        maxDrawdown = drawdown;
                        maxDDStart = peakTime;
                        maxDDEnd = candles[i].OpenTime;
                    }
                }
            }

            // 计算夏普比率（假设无风险利率为0）
            decimal? sharpeRatio = null;
            if (stdDev > 0)
            {
                var annualizedReturn = avgReturn * 365;  // 年化收益率（虚拟币365天）
                sharpeRatio = annualizedReturn / (stdDev * (decimal)Math.Sqrt(365));
            }

            return new CryptoVolatilityMetrics
            {
                Symbol = symbol.ToUpperInvariant(),
                AnnualizedVolatility = annualizedVol,
                DailyVolatility = dailyVol,
                AverageTrueRange = atr,
                MaxDrawdown = maxDrawdown,
                MaxDrawdownStartTime = maxDDStart,
                MaxDrawdownEndTime = maxDDEnd,
                SharpeRatio = sharpeRatio,
                PeriodDays = days,
                StandardDeviation = stdDev * 100,
                AverageReturn = avgReturn * 100
            };
        }
        catch (FriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取波动性指标失败: {Symbol}", symbol);
            throw new FriendlyException($"获取波动性指标失败: {ex.Message}", ex);
        }
    }

    // ==================== AI Functions ====================

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetOHLCVAsync);
        yield return AIFunctionFactory.Create(GetOrderBookDepthAsync);
        yield return AIFunctionFactory.Create(GetRecentTradesAsync);
        yield return AIFunctionFactory.Create(GetMarketMetricsAsync);
        yield return AIFunctionFactory.Create(GetVolumeDistributionAsync);
        yield return AIFunctionFactory.Create(GetVolatilityMetricsAsync);
    }

    // ==================== 辅助方法 ====================

    private async Task<string> GetCoinGeckoIdAsync(string symbol)
    {
        try
        {
            // 1. 预处理：如果是交易对格式（如 BTC/USDT），先提取基础币种（BTC）
            // 使用 ToBinanceFormat 清理分隔符，再提取 BaseCurrency
            var cleanSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);
            var searchSymbol = CryptoSymbolConverter.ExtractBaseCurrency(cleanSymbol);

            // 2. 搜索 CoinGecko
            var response = await _coinGeckoService.SearchCoinsAsync(searchSymbol);

            var coin = response?.Coins?.FirstOrDefault(c =>
                c.Symbol?.Equals(searchSymbol, StringComparison.OrdinalIgnoreCase) == true);

            if (coin != null)
            {
                return coin.Id;
            }

            // 3. Fallback: 使用提取后的符号小写
            _logger.LogWarning("未找到代币 {Symbol} ({SearchSymbol}) 的CoinGecko ID，使用小写symbol作为fallback", symbol, searchSymbol);
            return searchSymbol.ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索CoinGecko ID失败: {Symbol}", symbol);
            return symbol.ToLowerInvariant().Replace("/", "");
        }
    }
}
