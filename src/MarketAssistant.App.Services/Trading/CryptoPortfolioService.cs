using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Crypto;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 统一封装虚拟币账户资产估值与持仓快照，避免在多个调用点重复拼装账户视图。
/// </summary>
public class CryptoPortfolioService
{
    /// <summary>
    /// 账户概览缓存时长。仅供 UI 展示复用；风控与 AI 仓位封顶等资金安全路径
    /// 必须实时查询（useCache: false），1 秒级价格 tick 下陈旧快照会让连发订单绕过仓位上限。
    /// </summary>
    private static readonly TimeSpan AccountSummaryCacheTtl = TimeSpan.FromSeconds(3);

    private readonly IExchangeClient _exchangeClient;
    private readonly BinanceMarketDataService _marketDataService;
    private readonly TradingDataService _tradingDataService;
    private readonly TradingEnvironmentService _environmentService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CryptoPortfolioService> _logger;

    public CryptoPortfolioService(
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        BinanceMarketDataService marketDataService,
        TradingDataService tradingDataService,
        TradingEnvironmentService environmentService,
        IMemoryCache memoryCache,
        ILogger<CryptoPortfolioService> logger)
    {
        _exchangeClient = exchangeClient;
        _marketDataService = marketDataService;
        _tradingDataService = tradingDataService;
        _environmentService = environmentService;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// 获取账户估值概览。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <param name="useCache">是否允许使用 3 秒缓存。仅 UI 展示可传 true；
    /// 风控仓位校验、AI 仓位封顶等资金安全路径必须传 false 走实时查询。</param>
    public virtual async Task<AccountBalanceSummary> GetAccountBalanceSummaryAsync(CancellationToken ct = default, bool useCache = true)
    {
        var cacheKey = CacheKeys.GetCryptoAccountSummaryKey(_environmentService.CurrentMode);
        if (useCache && _memoryCache.TryGetValue(cacheKey, out AccountBalanceSummary? cached) && cached != null)
            return cached;

        var accountInfo = await _exchangeClient.GetAccountInfoAsync(ct);
        var summary = await BuildBalanceSummaryAsync(accountInfo, ct);
        _memoryCache.Set(cacheKey, summary, AccountSummaryCacheTtl);
        return summary;
    }

    /// <remarks>virtual 供单元测试替换（AISignal 硬性边界行为测试）。</remarks>
    public virtual async Task<List<PositionInfo>> GetCurrentPositionsAsync(CancellationToken ct = default)
    {
        // 合约模式无现货余额语义：直接映射交易所持仓（多空归一为绝对数量 + 方向无关的展示）。
        if (_exchangeClient.IsFutures)
            return await GetFuturesPositionsAsync(ct).ConfigureAwait(false);

        return await GetSpotPositionsAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<PositionInfo>> GetFuturesPositionsAsync(CancellationToken ct)
    {
        var positions = new List<PositionInfo>();

        List<ExchangePosition> exchangePositions;
        try
        {
            exchangePositions = await _exchangeClient.GetPositionsAsync(null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询合约持仓失败，返回空持仓视图");
            return positions;
        }

        foreach (var exchangePosition in exchangePositions)
        {
            var quantity = Math.Abs(exchangePosition.PositionAmt);
            if (quantity <= 0 || string.IsNullOrWhiteSpace(exchangePosition.Symbol))
                continue;

            var position = new PositionInfo
            {
                Symbol = exchangePosition.Symbol,
                Quantity = quantity,
                EntryPrice = exchangePosition.EntryPrice,
                CurrentPrice = exchangePosition.MarkPrice > 0 ? exchangePosition.MarkPrice : exchangePosition.EntryPrice,
            };

            if (position.EntryPrice > 0 && position.CurrentPrice > 0)
            {
                var direction = exchangePosition.PositionAmt > 0 ? 1m : -1m;
                // 杠杆为 0（交易所未返回）时按 1 倍兜底
                var leverage = exchangePosition.Leverage > 0 ? exchangePosition.Leverage : 1m;
                position.UnrealizedPnl = (position.CurrentPrice - position.EntryPrice) * position.Quantity * direction;
                // 百分比为含杠杆的收益率（ROE）口径：价格变动比例 × 杠杆 × 方向
                position.UnrealizedPnlPercent = (position.CurrentPrice - position.EntryPrice) / position.EntryPrice * 100 * direction * leverage;
            }

            positions.Add(position);
        }

        return positions;
    }

    private async Task<List<PositionInfo>> GetSpotPositionsAsync(CancellationToken ct)
    {
        var accountInfo = await _exchangeClient.GetAccountInfoAsync(ct);
        var positions = new List<PositionInfo>();

        foreach (var balance in accountInfo.Balances)
        {
            var quantity = balance.Free + balance.Locked;
            if (quantity <= 0 || balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 非 USDT 计价资产不拼凑 USDT 交易对：无对应行情时跳过，避免幽灵持仓。
            var symbol = CryptoSymbolConverter.ToBinanceFormat(balance.Asset);
            var ticker = await TryGetTickerAsync(symbol, ct).ConfigureAwait(false);
            if (ticker == null)
            {
                _logger.LogDebug("跳过无 USDT 行情的现货资产持仓: {Asset}", balance.Asset);
                continue;
            }

            var position = new PositionInfo
            {
                Symbol = symbol,
                Quantity = quantity
            };

            position.CurrentPrice = ticker.LastPrice;

            // 只按未平仓的 FIFO 持仓计算均价，避免已平仓记录污染浮盈显示
            var avgEntry = await _tradingDataService.GetOpenPositionAvgEntryPriceAsync(position.Symbol, ct);
            position.EntryPrice = avgEntry > 0 ? avgEntry : ticker.LastPrice;

            if (position.EntryPrice > 0)
            {
                position.UnrealizedPnl = (position.CurrentPrice - position.EntryPrice) * position.Quantity;
                position.UnrealizedPnlPercent = (position.CurrentPrice - position.EntryPrice) / position.EntryPrice * 100;
            }

            positions.Add(position);
        }

        return positions;
    }

    private async Task<Binance24hrTicker?> TryGetTickerAsync(string symbol, CancellationToken ct)
    {
        try
        {
            return await _marketDataService.Get24hrTickerAsync(symbol, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            _logger.LogDebug("获取持仓价格失败，跳过: {Symbol}", symbol);
            return null;
        }
    }

    public static decimal GetUsdtBalance(AccountBalanceSummary summary)
    {
        return summary.Assets
            .Where(asset => asset.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            .Sum(asset => asset.Free + asset.Locked);
    }

    private async Task<AccountBalanceSummary> BuildBalanceSummaryAsync(
        ExchangeAccountInfo accountInfo,
        CancellationToken ct)
    {
        var summary = new AccountBalanceSummary();

        foreach (var balance in accountInfo.Balances)
        {
            var totalAmount = balance.Free + balance.Locked;
            if (totalAmount <= 0)
            {
                continue;
            }

            var assetBalance = new AssetBalance
            {
                Asset = balance.Asset,
                Free = balance.Free,
                Locked = balance.Locked
            };

            if (balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            {
                assetBalance.ValueUSDT = totalAmount;
            }
            else
            {
                try
                {
                    // 与持仓视图同源：统一走 CryptoSymbolConverter，包装币（WBTC 等）可正确拼出计价对
                    var symbol = CryptoSymbolConverter.ToBinanceFormat(balance.Asset);
                    var ticker = await _marketDataService.Get24hrTickerAsync(symbol, ct);
                    if (ticker != null)
                    {
                        assetBalance.ValueUSDT = totalAmount * ticker.LastPrice;
                    }
                }
                catch (HttpRequestException)
                {
                    _logger.LogDebug("跳过无 USDT 标的行情的资产估值: {Asset}", balance.Asset);
                }
            }

            summary.TotalValueUSDT += assetBalance.ValueUSDT;
            summary.Assets.Add(assetBalance);
        }

        return summary;
    }
}
