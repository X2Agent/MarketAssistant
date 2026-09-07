using MarketAssistant.Applications.Cache;
using MarketAssistant.DataProviders;
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
        var accountInfo = await _exchangeClient.GetAccountInfoAsync(ct);
        var positions = new List<PositionInfo>();

        foreach (var balance in accountInfo.Balances)
        {
            var quantity = balance.Free + balance.Locked;
            if (quantity <= 0 || balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var position = new PositionInfo
            {
                Symbol = $"{balance.Asset}USDT",
                Quantity = quantity
            };

            try
            {
                var ticker = await _marketDataService.Get24hrTickerAsync(position.Symbol, ct);
                if (ticker != null)
                {
                    position.CurrentPrice = ticker.LastPrice;

                    // 只按未平仓的 FIFO 持仓计算均价，避免已平仓记录污染浮盈显示
                    var avgEntry = await _tradingDataService.GetOpenPositionAvgEntryPriceAsync(position.Symbol, ct);
                    position.EntryPrice = avgEntry > 0 ? avgEntry : ticker.LastPrice;

                    if (position.EntryPrice > 0)
                    {
                        position.UnrealizedPnl = (position.CurrentPrice - position.EntryPrice) * position.Quantity;
                        position.UnrealizedPnlPercent = (position.CurrentPrice - position.EntryPrice) / position.EntryPrice * 100;
                    }
                }
            }
            catch (HttpRequestException)
            {
                _logger.LogDebug("获取持仓价格失败，跳过: {Symbol}", position.Symbol);
            }

            positions.Add(position);
        }

        return positions;
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
                    var ticker = await _marketDataService.Get24hrTickerAsync($"{balance.Asset}USDT", ct);
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
