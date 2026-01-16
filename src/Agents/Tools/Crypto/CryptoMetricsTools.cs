using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Agents.Tools.Models.Crypto.CoinGecko;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Net.Http.Json;
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
    private const string BinanceBaseUrl = "https://api.binance.com/api/v3";
    private const string CoinGeckoBaseUrl = "https://api.coingecko.com/api/v3";

    private readonly HttpClient _httpClient;
    private readonly ILogger<CryptoMetricsTools> _logger;

    public CryptoMetricsTools(HttpClient httpClient, ILogger<CryptoMetricsTools> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // ==================== 市场深度数据（币安） ====================

    /// <summary>
    /// 获取历史K线数据（OHLCV）
    /// </summary>
    [Description("获取历史K线数据（开盘、最高、最低、收盘、成交量），支持多种时间间隔，用于技术分析和趋势判断")]
    public async Task<CryptoOHLCV> GetOHLCVAsync(string symbol, string interval = "1d", int limit = 500, long? startTime = null, long? endTime = null)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);
            var url = $"{BinanceBaseUrl}/klines?symbol={binanceSymbol}&interval={interval}&limit={limit}";

            if (startTime.HasValue)
                url += $"&startTime={startTime.Value}";
            if (endTime.HasValue)
                url += $"&endTime={endTime.Value}";

            var response = await _httpClient.GetFromJsonAsync<JsonArray>(url);
            if (response == null || response.Count == 0)
            {
                throw new InvalidOperationException($"未找到交易对 {binanceSymbol} 的K线数据");
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
                Interval = interval,
                Candles = candles
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取K线数据失败: {Symbol}, Interval: {Interval}", symbol, interval);
            throw;
        }
    }

    /// <summary>
    /// 获取订单簿深度数据
    /// </summary>
    [Description("获取订单簿深度数据，包括买卖盘挂单、价差、流动性等信息，用于分析支撑压力位")]
    public async Task<CryptoOrderBookDepth> GetOrderBookDepthAsync(string symbol, int limit = 100)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);
            var url = $"{BinanceBaseUrl}/depth?symbol={binanceSymbol}&limit={limit}";

            var response = await _httpClient.GetFromJsonAsync<JsonObject>(url);
            if (response == null)
            {
                throw new InvalidOperationException($"未找到交易对 {binanceSymbol} 的深度数据");
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取订单簿深度失败: {Symbol}", symbol);
            throw;
        }
    }

    /// <summary>
    /// 获取最近成交数据
    /// </summary>
    [Description("获取最近成交记录，分析买卖力量对比和成交活跃度")]
    public async Task<CryptoRecentTrades> GetRecentTradesAsync(string symbol, int limit = 500)
    {
        try
        {
            var binanceSymbol = CryptoSymbolConverter.ToBinanceFormat(symbol);
            var url = $"{BinanceBaseUrl}/trades?symbol={binanceSymbol}&limit={limit}";

            var response = await _httpClient.GetFromJsonAsync<JsonArray>(url);
            if (response == null || response.Count == 0)
            {
                throw new InvalidOperationException($"未找到交易对 {binanceSymbol} 的成交数据");
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最近成交失败: {Symbol}", symbol);
            throw;
        }
    }

    // ==================== 综合市场指标（CoinGecko） ====================

    /// <summary>
    /// 获取综合市场指标
    /// </summary>
    [Description("获取综合市场指标，包括市值、供应量、价格变动、排名、历史高低点等，用于资产估值")]
    public async Task<CryptoMarketMetrics> GetMarketMetricsAsync(string symbol)
    {
        try
        {
            var coinId = await GetCoinGeckoIdAsync(symbol);
            var url = $"{CoinGeckoBaseUrl}/coins/markets?vs_currency=usd&ids={coinId}&order=market_cap_desc&sparkline=false&price_change_percentage=24h,7d,30d";

            var response = await _httpClient.GetFromJsonAsync<JsonArray>(url);
            if (response == null || response.Count == 0)
            {
                throw new InvalidOperationException($"未找到代币 {symbol} 的市场数据");
            }

            var data = response[0] as JsonObject;
            if (data == null)
            {
                throw new InvalidOperationException($"解析代币 {symbol} 的市场数据失败");
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取市场指标失败: {Symbol}", symbol);
            throw;
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
            var url = $"{CoinGeckoBaseUrl}/coins/{coinId}/tickers?include_exchange_logo=false";

            var response = await _httpClient.GetFromJsonAsync<CoinGeckoTickersResponse>(url);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取交易量分布失败: {Symbol}", symbol);
            throw;
        }
    }

    // ==================== 衍生计算指标 ====================

    /// <summary>
    /// 获取波动性指标
    /// </summary>
    [Description("获取波动性指标，包括历史波动率、ATR、最大回撤、夏普比率等，用于风险评估")]
    public async Task<CryptoVolatilityMetrics> GetVolatilityMetricsAsync(string symbol, int days = 30)
    {
        try
        {
            // 获取历史K线数据
            var ohlcv = await GetOHLCVAsync(symbol, "1d", days + 1);
            if (ohlcv.Candles.Count < 2)
            {
                throw new InvalidOperationException($"数据不足，无法计算波动性指标");
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
            var annualizedVol = dailyVol * (decimal)Math.Sqrt(252);  // 年化（252个交易日）
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
                var annualizedReturn = avgReturn * 252;  // 年化收益率
                sharpeRatio = annualizedReturn / (stdDev * (decimal)Math.Sqrt(252));
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取波动性指标失败: {Symbol}", symbol);
            throw;
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
            var searchUrl = $"{CoinGeckoBaseUrl}/search?query={symbol}";
            var response = await _httpClient.GetFromJsonAsync<CoinGeckoSearchResponse>(searchUrl);

            var coin = response?.Coins?.FirstOrDefault(c =>
                c.Symbol?.Equals(symbol, StringComparison.OrdinalIgnoreCase) == true);

            if (coin != null)
            {
                return coin.Id;
            }

            _logger.LogWarning("未找到代币 {Symbol} 的CoinGecko ID，使用小写symbol作为fallback", symbol);
            return symbol.ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索CoinGecko ID失败: {Symbol}", symbol);
            return symbol.ToLowerInvariant();
        }
    }
}
