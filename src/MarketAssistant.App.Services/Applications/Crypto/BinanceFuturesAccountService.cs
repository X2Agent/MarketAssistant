using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安 U 本位合约账户服务（实盘/Testnet 共用，通过 httpClientName 与 label 参数化）。
/// 端点：/fapi/v2/account、/fapi/v1/order、/fapi/v1/openOrders、/fapi/v2/positionRisk
/// API文档：https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info
/// </summary>
public sealed class BinanceFuturesAccountService : BinanceAccountServiceBase
{
    public BinanceFuturesAccountService(
        IHttpClientFactory httpClientFactory,
        ILogger<BinanceFuturesAccountService> logger,
        IBinanceAuthService authService,
        string httpClientName,
        string label)
        : base(httpClientFactory, logger, authService, httpClientName, label)
    {
    }

    protected override string AccountEndpoint => "/fapi/v2/account";
    protected override string OrderEndpoint => "/fapi/v1/order";
    protected override string OpenOrdersEndpoint => "/fapi/v1/openOrders";

    /// <summary>
    /// 合约下单附加 positionSide（单向模式 BOTH / 双向模式 LONG/SHORT）。
    /// </summary>
    protected override void AppendPlaceOrderParameters(Dictionary<string, string> parameters, string? positionSide)
    {
        if (!string.IsNullOrEmpty(positionSide))
            parameters["positionSide"] = positionSide.ToUpper();
    }

    /// <summary>
    /// 解析合约账户响应：将合约资产结构映射为统一的 <see cref="BinanceAccountInfo"/>。
    /// Free = 可用余额，Locked = 钱包余额 - 可用余额。
    /// </summary>
    protected override async Task<BinanceAccountInfo> ParseAccountInfoAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var futuresAccount = await content.ReadFromJsonAsync<BinanceFuturesAccountResponse>(cancellationToken)
            ?? throw new FriendlyException($"解析{Label}合约账户信息失败");

        return new BinanceAccountInfo
        {
            CanTrade = futuresAccount.CanTrade,
            Balances = futuresAccount.Assets
                .Where(a => decimal.TryParse(a.WalletBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
                            decimal.Parse(a.WalletBalance, NumberStyles.Number, CultureInfo.InvariantCulture) != 0)
                .Select(a =>
                {
                    var wallet = decimal.Parse(a.WalletBalance, NumberStyles.Number, CultureInfo.InvariantCulture);
                    var available = decimal.Parse(a.AvailableBalance, NumberStyles.Number, CultureInfo.InvariantCulture);
                    return new BinanceBalance
                    {
                        Asset = a.Asset,
                        Free = a.AvailableBalance,
                        Locked = (wallet - available).ToString("F8", CultureInfo.InvariantCulture)
                    };
                }).ToList()
        };
    }

    protected override async Task<BinanceOrderResponse> ParseOrderResponseAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var futuresOrder = await content.ReadFromJsonAsync<BinanceFuturesOrderResponse>(cancellationToken)
            ?? throw new FriendlyException($"解析{Label}合约订单响应失败");
        return MapFuturesOrderResponse(futuresOrder);
    }

    protected override async Task<List<BinanceOrderResponse>> ParseOrderListResponseAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var futuresOrders = await content.ReadFromJsonAsync<List<BinanceFuturesOrderResponse>>(cancellationToken) ?? [];
        return futuresOrders.Select(MapFuturesOrderResponse).ToList();
    }

    /// <summary>
    /// 查询当前合约持仓信息（含方向、未实现盈亏、杠杆、保证金模式）。
    /// 端点：GET /fapi/v2/positionRisk
    /// </summary>
    internal async Task<List<BinanceFuturesPositionRisk>> GetPositionInfoAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryString = string.IsNullOrEmpty(symbol) ? "" : $"symbol={symbol.ToUpper()}";
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"/fapi/v2/positionRisk?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AuthService.AddAuthHeaders(request);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}查询合约持仓", cancellationToken);

            return await response.Content.ReadFromJsonAsync<List<BinanceFuturesPositionRisk>>(cancellationToken) ?? [];
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Label}查询合约持仓失败 - 网络错误", Label);
            throw new FriendlyException($"{Label}查询合约持仓失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            Logger.LogError(ex, "{Label}查询合约持仓失败", Label);
            throw new FriendlyException($"{Label}查询合约持仓失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 将合约订单响应映射为统一的 BinanceOrderResponse。
    /// 合约 cumQuote 字段对应现货 cummulativeQuoteQty。
    /// </summary>
    private static BinanceOrderResponse MapFuturesOrderResponse(BinanceFuturesOrderResponse futures)
    {
        return new BinanceOrderResponse
        {
            Symbol = futures.Symbol,
            OrderId = futures.OrderId,
            ClientOrderId = futures.ClientOrderId,
            Price = futures.Price,
            OrigQty = futures.OrigQty,
            ExecutedQty = futures.ExecutedQty,
            CummulativeQuoteQty = futures.CumQuote,
            Status = futures.Status,
            TimeInForce = futures.TimeInForce,
            Type = futures.Type,
            Side = futures.Side,
            Fills = [] // 合约下单响应不含 fills 数组
        };
    }
}

#region 合约响应模型

/// <summary>
/// 币安合约账户响应（/fapi/v2/account）
/// </summary>
internal sealed class BinanceFuturesAccountResponse
{
    public bool CanTrade { get; set; }
    public bool CanDeposit { get; set; }
    public bool CanWithdraw { get; set; }
    public long UpdateTime { get; set; }
    public List<BinanceFuturesAsset> Assets { get; set; } = [];
}

internal sealed class BinanceFuturesAsset
{
    public string Asset { get; set; } = string.Empty;
    public string WalletBalance { get; set; } = string.Empty;
    public string MarginBalance { get; set; } = string.Empty;
    public string AvailableBalance { get; set; } = string.Empty;
    public string UnrealizedProfit { get; set; } = string.Empty;
}

/// <summary>
/// 币安合约订单响应（/fapi/v1/order）
/// 注意：合约字段 cumQuote 对应现货 cummulativeQuoteQty
/// </summary>
internal sealed class BinanceFuturesOrderResponse
{
    public string Symbol { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public string ClientOrderId { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("avgPrice")]
    public string AvgPrice { get; set; } = string.Empty;

    public string OrigQty { get; set; } = string.Empty;
    public string ExecutedQty { get; set; } = string.Empty;

    [JsonPropertyName("cumQuote")]
    public string CumQuote { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
    public string TimeInForce { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("positionSide")]
    public string PositionSide { get; set; } = string.Empty;

    public long UpdateTime { get; set; }
}

/// <summary>
/// 币安合约持仓信息（/fapi/v2/positionRisk）
/// </summary>
internal sealed class BinanceFuturesPositionRisk
{
    public string Symbol { get; set; } = string.Empty;
    public string PositionAmt { get; set; } = string.Empty;
    public string EntryPrice { get; set; } = string.Empty;
    public string MarkPrice { get; set; } = string.Empty;
    public string UnRealizedProfit { get; set; } = string.Empty;
    public string LiquidationPrice { get; set; } = string.Empty;
    public string Leverage { get; set; } = string.Empty;
    public string MaxNotionalValue { get; set; } = string.Empty;

    [JsonPropertyName("marginType")]
    public string MarginType { get; set; } = string.Empty;

    public string IsolatedMargin { get; set; } = string.Empty;

    [JsonPropertyName("positionSide")]
    public string PositionSide { get; set; } = string.Empty;

    public long UpdateTime { get; set; }
}

#endregion
