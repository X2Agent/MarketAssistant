using System.Net.Http.Json;

namespace MarketAssistant.Applications.Crypto;

public sealed partial class BinanceFuturesAccountService
{
    public override async Task<List<BinanceOrderResponse>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        var orders = await base.GetOpenOrdersAsync(symbol, cancellationToken);
        var algos = await SendSignedRequestAsync<List<FuturesAlgoOrder>>(HttpMethod.Get, "openAlgoOrders", symbol == null ? "" : $"symbol={symbol}", cancellationToken);
        orders.AddRange(algos.Select(order => order.ToOrder()));
        return orders;
    }

    public override async Task<BinanceOrderResponse> GetOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        if (orderId >= 0) return await base.GetOrderAsync(symbol, orderId, cancellationToken);
        var algo = await SendSignedRequestAsync<FuturesAlgoOrder>(HttpMethod.Get, "algoOrder", $"algoId={-orderId}", cancellationToken);
        if (!long.TryParse(algo.ActualOrderId.ToString(), out var childId) || childId <= 0) return algo.ToOrder();
        var fill = await base.GetOrderAsync(symbol, childId, cancellationToken);
        fill.OrderId = orderId;
        fill.ClientOrderId = algo.ClientAlgoId;
        return fill;
    }

    public override async Task<BinanceOrderResponse> CancelOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        if (orderId >= 0) return await base.CancelOrderAsync(symbol, orderId, cancellationToken);
        await SendSignedRequestAsync<JsonElement>(HttpMethod.Delete, "algoOrder", $"algoId={-orderId}", cancellationToken);
        return await GetOrderAsync(symbol, orderId, cancellationToken);
    }

    private async Task<T> SendSignedRequestAsync<T>(HttpMethod method, string endpoint, string query, CancellationToken ct)
    {
        var signed = await AuthService.SignQueryStringAsync(query, ct);
        using var request = new HttpRequestMessage(method, $"/fapi/v1/{endpoint}?{signed}");
        AuthService.AddAuthHeaders(request);
        using var client = HttpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct);
        await EnsureSuccessWithBinanceErrorAsync(response, endpoint, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new JsonException($"{endpoint} 响应为空");
    }

    internal async Task EnsureOneWayModeAsync(CancellationToken ct)
    {
        var mode = await SendSignedRequestAsync<JsonElement>(HttpMethod.Get, "positionSide/dual", "", ct);
        if (mode.GetProperty("dualSidePosition").GetBoolean()) throw new FriendlyException("合约交易仅支持单向持仓模式");
    }
}

internal sealed record FuturesAlgoOrder(long AlgoId, string ClientAlgoId, string Symbol, string Side, string OrderType,
    string Quantity, string TriggerPrice, string AlgoStatus, JsonElement ActualOrderId)
{
    // 两套 API 的数值 ID 不在同一命名空间。本地负数标识 algo 单；发送给交易所时还原正数。
    internal BinanceOrderResponse ToOrder() => new()
    {
        OrderId = -AlgoId,
        ClientOrderId = ClientAlgoId,
        Symbol = Symbol,
        Side = Side,
        Type = OrderType,
        OrigQty = Quantity,
        StopPrice = TriggerPrice,
        Status = AlgoStatus switch
        {
            "CANCELED" or "REJECTED" or "EXPIRED" => AlgoStatus,
            _ => "NEW"
        }
    };
}
