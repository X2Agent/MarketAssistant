using System.Globalization;
using System.Net.Http.Json;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安现货账户服务 - OCO 保护单部分。
/// 与账户/订单主体拆分为 partial 文件：双腿 OCO 的数量与触发价取整、签名下单
/// 是独立于账户查询的职责，单独成文件便于评审（文件规模护栏）。
/// </summary>
public sealed partial class BinanceSpotAccountService
{
    /// <summary>
    /// 现货保护性 OCO：止盈腿（TAKE_PROFIT）与止损腿（STOP_LOSS）均为触发即市价，
    /// 无需限价；数量与触发价按交易对过滤器取整。挂出时需满足
    /// 止盈触发价 &gt; 最新价 &gt; 止损触发价（SELL 方向），由调用方保证。
    /// </summary>
    public override async Task<BinanceOcoResponse> PlaceSpotOcoAsync(
        string symbol, string side, decimal quantity,
        string? stopClientOrderId, decimal stopPrice,
        string? takeProfitClientOrderId, decimal takeProfitPrice,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithFriendlyErrorsAsync("挂 OCO 保护单", async () =>
        {
            _filterCache ??= new ExchangeSymbolFilterCache(HttpClientFactory, Logger);
            var filters = await _filterCache.GetFiltersAsync(
                symbol, HttpClientName, ExchangeInfoEndpoint, cancellationToken);

            var roundedQuantity = ExchangeSymbolFilterCache.RoundQuantityToStep(quantity, filters?.StepSize) ?? quantity;
            if (roundedQuantity <= 0)
            {
                throw new FriendlyException(
                    $"OCO 数量 {quantity} 经 {symbol} 精度取整后为 0（stepSize={filters?.StepSize}），已拒绝下单");
            }

            var roundedStopPrice = ExchangeSymbolFilterCache.RoundPriceToTick(stopPrice, filters?.TickSize) ?? stopPrice;
            var roundedTakeProfitPrice = ExchangeSymbolFilterCache.RoundPriceToTick(takeProfitPrice, filters?.TickSize) ?? takeProfitPrice;

            var parameters = new Dictionary<string, string>
            {
                ["symbol"] = symbol.ToUpperInvariant(),
                ["side"] = side.ToUpperInvariant(),
                ["quantity"] = roundedQuantity.ToString("F8", CultureInfo.InvariantCulture),
                // SELL 方向：above 为止盈（价格涨到触发价，市价卖出），below 为止损（价格跌破触发价，市价卖出）
                ["aboveType"] = "TAKE_PROFIT",
                ["aboveStopPrice"] = roundedTakeProfitPrice.ToString("F8", CultureInfo.InvariantCulture),
                ["belowType"] = "STOP_LOSS",
                ["belowStopPrice"] = roundedStopPrice.ToString("F8", CultureInfo.InvariantCulture)
            };
            if (!string.IsNullOrEmpty(stopClientOrderId))
                parameters["belowClientOrderId"] = stopClientOrderId;
            if (!string.IsNullOrEmpty(takeProfitClientOrderId))
                parameters["aboveClientOrderId"] = takeProfitClientOrderId;

            var queryString = string.Join("&",
                parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"/api/v3/orderList/oco?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            AuthService.AddAuthHeaders(request);

            Logger.LogInformation("正在{Label}挂 OCO 保护单: {Symbol} {Side} 数量:{Quantity} 止损:{Stop} 止盈:{TakeProfit}",
                Label, symbol, side, roundedQuantity, roundedStopPrice, roundedTakeProfitPrice);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}挂 OCO 保护单", cancellationToken);

            var ocoResponse = await response.Content.ReadFromJsonAsync<BinanceOcoResponse>(cancellationToken)
                ?? throw new FriendlyException($"解析{Label}OCO 响应失败");
            return ocoResponse;
        });
    }
}
