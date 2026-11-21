using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MarketAssistant.Infrastructure;
using MarketAssistant.Trading.Abstractions;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安账户服务基类，封装现货/合约共用的 HTTP 调用、签名、错误处理与双层 catch 模板。
/// 子类只需提供端点前缀与响应映射逻辑。
/// positionSide 参数仅在合约下单时使用，现货下单忽略。
/// </summary>
/// <param name="httpClientFactory">HttpClient 工厂</param>
/// <param name="logger">日志器</param>
/// <param name="authService">鉴权服务（决定实盘/Testnet 密钥来源）</param>
/// <param name="httpClientName">HttpClient 名称（"Binance" / "BinanceSpotDemo" / "BinanceFutures" / "BinanceFuturesTestnet"）</param>
/// <param name="label">错误提示前缀（"" / "Demo " / "Testnet "）</param>
public abstract class BinanceAccountServiceBase(
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    IBinanceAuthService authService,
    string httpClientName,
    string label)
{
    protected readonly IHttpClientFactory HttpClientFactory = httpClientFactory;
    protected readonly ILogger Logger = logger;
    protected readonly IBinanceAuthService AuthService = authService;
    protected readonly string HttpClientName = httpClientName;
    protected readonly string Label = label;

    /// <summary>
    /// 交易对精度过滤器缓存（进程内共享，按 HttpClient 名称维度隔离）。
    /// 下单时用于对数量/价格取整，避免精度违规被交易所拒单。
    /// </summary>
    protected ExchangeSymbolFilterCache? _filterCache;

    /// <summary>
    /// 账户信息端点（如 /api/v3/account、/fapi/v2/account）
    /// </summary>
    protected abstract string AccountEndpoint { get; }

    /// <summary>
    /// 订单端点（如 /api/v3/order、/fapi/v1/order）
    /// </summary>
    protected abstract string OrderEndpoint { get; }

    /// <summary>
    /// 挂单端点（如 /api/v3/openOrders、/fapi/v1/openOrders）
    /// </summary>
    protected abstract string OpenOrdersEndpoint { get; }

    /// <summary>
    /// 交易对精度过滤器端点（现货 /api/v3/exchangeInfo，合约 /fapi/v1/exchangeInfo）
    /// </summary>
    protected abstract string ExchangeInfoEndpoint { get; }

    /// <summary>
    /// 是否为合约 API。决定 post-only 的形态：合约是 LIMIT + timeInForce=GTX，
    /// 现货是独立的 LIMIT_MAKER 订单类型（现货 timeInForce 枚举只有 GTC/IOC/FOK，没有 GTX）。
    /// </summary>
    protected abstract bool IsFuturesApi { get; }

    /// <summary>
    /// 解析账户信息响应。子类负责将合约/现货特定结构映射为统一的 <see cref="BinanceAccountInfo"/>。
    /// </summary>
    protected abstract Task<BinanceAccountInfo> ParseAccountInfoAsync(HttpContent content, CancellationToken cancellationToken);

    /// <summary>
    /// 解析订单响应。子类负责将合约/现货特定结构映射为统一的 <see cref="BinanceOrderResponse"/>。
    /// </summary>
    protected abstract Task<BinanceOrderResponse> ParseOrderResponseAsync(HttpContent content, CancellationToken cancellationToken);

    /// <summary>
    /// 解析订单列表响应。
    /// </summary>
    protected abstract Task<List<BinanceOrderResponse>> ParseOrderListResponseAsync(HttpContent content, CancellationToken cancellationToken);

    /// <summary>
    /// 下单时附加特定参数（如合约的 positionSide）。默认无操作。
    /// </summary>
    protected virtual void AppendPlaceOrderParameters(Dictionary<string, string> parameters, string? positionSide)
    {
        // 现货无需额外参数；合约子类重写以添加 positionSide
    }

    /// <summary>
    /// 统一异常包装：网络错误给出简短提示，其余异常附带原始消息，FriendlyException 原样透传。
    /// 用户主动取消（OperationCanceledException 且非 HttpClient 超时）向上透传，保留取消语义。
    /// </summary>
    protected async Task<T> ExecuteWithFriendlyErrorsAsync<T>(string operation, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Label}{Operation}失败 - 网络错误", Label, operation);
            throw new FriendlyException($"{Label}{operation}失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is OperationCanceledException
                                   && ex.InnerException is not TimeoutException)
        {
            // HttpClient 超时在 .NET 5+ 抛 TaskCanceledException(InnerException=TimeoutException)，
            // 属可重试的瞬时故障，落到下面的通用包装（消息可读且保留内层供瞬时性判定）；
            // 其余取消（用户取消令牌、请求中途取消）原样抛出，由调用方按取消路径处理。
            throw;
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            Logger.LogError(ex, "{Label}{Operation}失败", Label, operation);
            throw new FriendlyException($"{Label}{operation}失败: {ex.Message}", ex);
        }
    }

    /// <inheritdoc cref="ExecuteWithFriendlyErrorsAsync{T}"/>
    protected Task ExecuteWithFriendlyErrorsAsync(string operation, Func<Task> action)
    {
        return ExecuteWithFriendlyErrorsAsync<object?>(operation, async () =>
        {
            await action();
            return null;
        });
    }

    /// <summary>
    /// 获取账户信息。子类通过 <see cref="ParseAccountInfoAsync"/> 提供响应解析。
    /// </summary>
    public async Task<BinanceAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithFriendlyErrorsAsync("获取账户信息", async () =>
        {
            var signedQuery = await AuthService.SignQueryStringAsync("", cancellationToken);
            var url = $"{AccountEndpoint}?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AuthService.AddAuthHeaders(request);

            Logger.LogInformation("正在获取{Label}账户信息...", Label);
            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}获取账户信息", cancellationToken);

            return await ParseAccountInfoAsync(response.Content, cancellationToken);
        });
    }

    /// <summary>
    /// 下单。先校验账户可交易，再按现货/合约端点签名调用。子类通过 <see cref="AppendPlaceOrderParameters"/> 注入特定参数。
    /// </summary>
    /// <param name="reduceOnly">合约平仓时传 true，确保只平仓不开新仓（仅合约有效，现货忽略）</param>
    /// <param name="stopPrice">条件单触发价（STOP_MARKET/TAKE_PROFIT_MARKET 必填）</param>
    /// <param name="trailingDelta">追踪止损回调比例（基点，1%=100，TRAILING_STOP_MARKET 必填）</param>
    public async Task<BinanceOrderResponse> PlaceOrderAsync(
        string symbol,
        string side,
        string type,
        decimal quantity,
        decimal? price = null,
        string? clientOrderId = null,
        string? positionSide = null,
        bool reduceOnly = false,
        decimal? stopPrice = null,
        int? trailingDelta = null,
        bool postOnly = false,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithFriendlyErrorsAsync("下单", async () =>
        {
            var accountInfo = await GetAccountInfoAsync(cancellationToken);
            if (!accountInfo.CanTrade)
            {
                throw new FriendlyException($"{Label}账户当前被限制交易，无法下单（可能因违规、KYC 未完成或地区限制）");
            }

            // 按交易所精度过滤器取整：数量向下取整到 stepSize，价格/触发价四舍五入到 tickSize；
            // 拿不到过滤器信息时保持原值，由交易所侧校验兜底
            _filterCache ??= new ExchangeSymbolFilterCache(HttpClientFactory, Logger);
            var filters = await _filterCache.GetFiltersAsync(symbol, HttpClientName, ExchangeInfoEndpoint, cancellationToken);

            var roundedQuantity = ExchangeSymbolFilterCache.RoundQuantityToStep(quantity, filters?.StepSize) ?? quantity;
            if (roundedQuantity != quantity)
            {
                Logger.LogDebug("下单数量按 stepSize {StepSize} 取整: {Original} → {Rounded} ({Symbol})",
                    filters?.StepSize, quantity, roundedQuantity, symbol);
            }

            // 取整后数量归零（如小于 stepSize）提交必被 -1013 拒绝且浪费请求，直接本地拒单
            if (roundedQuantity <= 0)
            {
                throw new FriendlyException(
                    $"下单数量 {quantity} 经 {symbol} 精度取整后为 0（stepSize={filters?.StepSize}），已拒绝下单");
            }

            var parameters = new Dictionary<string, string>
            {
                ["symbol"] = symbol.ToUpperInvariant(),
                ["side"] = side.ToUpperInvariant(),
                ["type"] = type.ToUpperInvariant(),
                ["quantity"] = roundedQuantity.ToString("F8", CultureInfo.InvariantCulture)
            };

            // 现货 post-only 是独立的 LIMIT_MAKER 订单类型（现货 timeInForce 枚举没有 GTX，
            // 传 GTX 会被交易所按参数错误拒绝）；合约 post-only 是 LIMIT + timeInForce=GTX。
            // 两种形态都保证"会立即吃单时拒单而不是转 taker"，成交必为 maker 费率。
            if (postOnly && !IsFuturesApi)
                parameters["type"] = "LIMIT_MAKER";

            if (!string.IsNullOrEmpty(clientOrderId))
                parameters["newClientOrderId"] = clientOrderId;

            AppendPlaceOrderParameters(parameters, positionSide);

            // 合约平仓时传入 reduceOnly=true，确保订单只减少持仓而不会开新仓
            if (reduceOnly)
                parameters["reduceOnly"] = "true";

            var typeUpper = parameters["type"];
            if (typeUpper is "LIMIT" or "LIMIT_MAKER")
            {
                if (!price.HasValue)
                {
                    throw new ArgumentException("限价单必须指定价格");
                }
                var roundedPrice = ExchangeSymbolFilterCache.RoundPriceToTick(price.Value, filters?.TickSize) ?? price.Value;
                if (roundedPrice != price.Value)
                {
                    Logger.LogDebug("限价单价格按 tickSize {TickSize} 取整: {Original} → {Rounded} ({Symbol})",
                        filters?.TickSize, price.Value, roundedPrice, symbol);
                }
                parameters["price"] = roundedPrice.ToString("F8", CultureInfo.InvariantCulture);
                // LIMIT_MAKER 不接受 timeInForce（post-only 语义由类型本身表达）；
                // GTX = 合约 post-only：价格会穿越盘口时被拒单而不是吃单，保证 maker 费率。
                if (typeUpper != "LIMIT_MAKER")
                    parameters["timeInForce"] = postOnly ? "GTX" : "GTC";
            }

            // 条件单参数
            if (stopPrice.HasValue
                && typeUpper is "STOP_MARKET" or "TAKE_PROFIT_MARKET" or "STOP_LOSS" or "TAKE_PROFIT"
                    or "STOP_LOSS_LIMIT" or "TAKE_PROFIT_LIMIT")
            {
                // 触发价同样受 tickSize 约束，否则条件单会被交易所以精度错误拒绝
                var roundedStopPrice = ExchangeSymbolFilterCache.RoundPriceToTick(stopPrice.Value, filters?.TickSize) ?? stopPrice.Value;
                parameters["stopPrice"] = roundedStopPrice.ToString("F8", CultureInfo.InvariantCulture);
            }

            if (trailingDelta.HasValue && typeUpper == "TRAILING_STOP_MARKET")
            {
                parameters["trailingDelta"] = trailingDelta.Value.ToString(CultureInfo.InvariantCulture);
            }

            var queryString = string.Join("&",
                parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"{OrderEndpoint}?{signedQuery}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            AuthService.AddAuthHeaders(request);

            Logger.LogInformation("正在{Label}下单: {Symbol} {Side} {Type} 数量:{Quantity}",
                Label, symbol, side, type, quantity);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}下单", cancellationToken);

            var orderResponse = await ParseOrderResponseAsync(response.Content, cancellationToken);

            Logger.LogInformation("{Label}下单成功，订单ID: {OrderId}, 状态: {Status}",
                Label, orderResponse.OrderId, orderResponse.Status);

            return orderResponse;
        });
    }

    /// <summary>
    /// 查询订单状态。
    /// </summary>
    /// <param name="lookupQuery">已拼好的查询串（orderId=... 或 origClientOrderId=...）</param>
    private async Task<BinanceOrderResponse> QueryOrderAsync(string symbol, string lookupQuery, CancellationToken cancellationToken)
    {
        return await ExecuteWithFriendlyErrorsAsync("查询订单", async () =>
        {
            var queryString = $"symbol={symbol.ToUpperInvariant()}&{lookupQuery}";
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"{OrderEndpoint}?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AuthService.AddAuthHeaders(request);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}查询订单", cancellationToken);

            return await ParseOrderResponseAsync(response.Content, cancellationToken);
        });
    }

    /// <summary>
    /// 按订单 ID 查询。
    /// </summary>
    public Task<BinanceOrderResponse> GetOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
        => QueryOrderAsync(symbol, $"orderId={orderId}", cancellationToken);

    /// <summary>
    /// 按客户端订单 ID 查询。用于网络异常后"订单是否已落地"的收敛查询：
    /// 币安对未找到的 clientOrderId 返回 -2013，调用方据此区分"确实没下成"与"下成了但响应丢失"。
    /// </summary>
    public Task<BinanceOrderResponse> GetOrderByClientOrderIdAsync(
        string symbol, string clientOrderId, CancellationToken cancellationToken = default)
        => QueryOrderAsync(symbol, $"origClientOrderId={Uri.EscapeDataString(clientOrderId)}", cancellationToken);

    /// <summary>
    /// 撤销订单。
    /// </summary>
    public async Task<BinanceOrderResponse> CancelOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithFriendlyErrorsAsync("取消订单", async () =>
        {
            var queryString = $"symbol={symbol.ToUpperInvariant()}&orderId={orderId}";
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"{OrderEndpoint}?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            AuthService.AddAuthHeaders(request);

            Logger.LogInformation("正在{Label}取消订单: {Symbol} OrderId:{OrderId}", Label, symbol, orderId);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}取消订单", cancellationToken);

            var orderResponse = await ParseOrderResponseAsync(response.Content, cancellationToken);

            Logger.LogInformation("{Label}取消订单成功，订单ID: {OrderId}", Label, orderId);
            return orderResponse;
        });
    }

    /// <summary>
    /// 查询当前挂单。
    /// </summary>
    public async Task<List<BinanceOrderResponse>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithFriendlyErrorsAsync("查询挂单", async () =>
        {
            var queryString = string.IsNullOrEmpty(symbol) ? "" : $"symbol={symbol.ToUpperInvariant()}";
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"{OpenOrdersEndpoint}?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AuthService.AddAuthHeaders(request);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}查询挂单", cancellationToken);

            return await ParseOrderListResponseAsync(response.Content, cancellationToken);
        });
    }

    /// <summary>
    /// 现货保护性 OCO（POST /api/v3/orderList/oco）：止盈腿（TAKE_PROFIT，触发即市价）与
    /// 止损腿（STOP_LOSS，触发即市价）共享同一数量，任一腿成交后交易所自动撤销另一腿。
    /// 默认抛出不支持——仅现货子类实现；合约的 reduceOnly 条件单不经过此方法。
    /// </summary>
    public virtual Task<BinanceOcoResponse> PlaceSpotOcoAsync(
        string symbol, string side, decimal quantity,
        string? stopClientOrderId, decimal stopPrice,
        string? takeProfitClientOrderId, decimal takeProfitPrice,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("OCO 订单仅现货支持");


    /// <summary>
    /// 统一的币安响应错误处理：读取响应体并解析币安错误码，
    /// 抛出包含详细信息的 FriendlyException，而非泛化的 HTTP 状态码错误。
    /// 币安错误响应格式：{"code":-1013,"msg":"..."}
    /// </summary>
    internal static async Task EnsureSuccessWithBinanceErrorAsync(
        HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var statusCode = (int)response.StatusCode;
        string errorDetail;
        int? binanceErrorCode = null;
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<BinanceApiError>(content);
            binanceErrorCode = error?.Code;
            errorDetail = error != null && !string.IsNullOrEmpty(error.Msg)
                ? $"[{error.Code}] {error.Msg}"
                : (string.IsNullOrEmpty(content) ? $"HTTP {statusCode}" : content);
        }
        catch
        {
            errorDetail = $"HTTP {statusCode}";
        }

        if (binanceErrorCode == -2013)
            throw new OrderNotFoundException($"{operation}失败: {errorDetail}");

        throw new FriendlyException($"{operation}失败: {errorDetail}");
    }

    private sealed class BinanceApiError
    {
        public int Code { get; set; }
        public string Msg { get; set; } = string.Empty;
    }
}

/// <summary>
/// 币安现货账户服务（实盘/Testnet 共用，通过 httpClientName 与 label 参数化）。
/// 端点：/api/v3/account、/api/v3/order、/api/v3/openOrders
/// API文档：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api/account-endpoints
/// </summary>
public sealed partial class BinanceSpotAccountService(
    IHttpClientFactory httpClientFactory,
    ILogger<BinanceSpotAccountService> logger,
    IBinanceAuthService authService,
    string httpClientName,
    string label) : BinanceAccountServiceBase(httpClientFactory, logger, authService, httpClientName, label)
{
    protected override string AccountEndpoint => "/api/v3/account";
    protected override string OrderEndpoint => "/api/v3/order";
    protected override string OpenOrdersEndpoint => "/api/v3/openOrders";
    protected override string ExchangeInfoEndpoint => "/api/v3/exchangeInfo";
    protected override bool IsFuturesApi => false;

    protected override async Task<BinanceAccountInfo> ParseAccountInfoAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var result = await content.ReadFromJsonAsync<BinanceAccountInfo>(cancellationToken);
        return result ?? throw new FriendlyException($"解析{Label}账户信息失败");
    }

    protected override async Task<BinanceOrderResponse> ParseOrderResponseAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var result = await content.ReadFromJsonAsync<BinanceOrderResponse>(cancellationToken);
        return result ?? throw new FriendlyException($"解析{Label}订单响应失败");
    }

    protected override async Task<List<BinanceOrderResponse>> ParseOrderListResponseAsync(HttpContent content, CancellationToken cancellationToken)
        => await content.ReadFromJsonAsync<List<BinanceOrderResponse>>(cancellationToken) ?? [];

}

#region 响应模型

/// <summary>
/// 币安账户信息（现货结构）
/// </summary>
public class BinanceAccountInfo
{
    public int MakerCommission { get; set; }
    public int TakerCommission { get; set; }
    public int BuyerCommission { get; set; }
    public int SellerCommission { get; set; }
    public bool CanTrade { get; set; }
    public bool CanWithdraw { get; set; }
    public bool CanDeposit { get; set; }
    public long UpdateTime { get; set; }
    public string AccountType { get; set; } = string.Empty;
    public List<BinanceBalance> Balances { get; set; } = [];
}

/// <summary>
/// 币安账户余额
/// </summary>
public class BinanceBalance
{
    public string Asset { get; set; } = string.Empty;
    public string Free { get; set; } = string.Empty;
    public string Locked { get; set; } = string.Empty;
}

/// <summary>
/// 币安订单响应
/// </summary>
public class BinanceOrderResponse
{
    public string Symbol { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public string ClientOrderId { get; set; } = string.Empty;
    public long TransactTime { get; set; }
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// 成交均价（合约 avgPrice 字段）。市价单 Price 为 0 时可用此值作为实际成交价。
    /// </summary>
    public string AvgPrice { get; set; } = string.Empty;

    public string OrigQty { get; set; } = string.Empty;
    public string ExecutedQty { get; set; } = string.Empty;
    public string CummulativeQuoteQty { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TimeInForce { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// 条件单触发价（StopMarket/TakeProfitMarket 订单）。
    /// </summary>
    public string StopPrice { get; set; } = string.Empty;

    /// <summary>
    /// 成交流水（仅 POST /api/v3/order 响应包含，查询接口不返回）。
    /// 用于汇总手续费。
    /// </summary>
    public List<BinanceFill> Fills { get; set; } = [];
}

/// <summary>
/// Binance 单笔成交流水
/// </summary>
public class BinanceFill
{
    public string Price { get; set; } = string.Empty;
    public string Qty { get; set; } = string.Empty;
    public string Commission { get; set; } = string.Empty;
    public string CommissionAsset { get; set; } = string.Empty;
}

/// <summary>
/// 币安现货 OCO（orderList）响应：orderListId + 两腿订单明细。
/// 撤销任一成员单即撤销整个列表；任一腿成交后交易所自动撤销另一腿。
/// </summary>
public class BinanceOcoResponse
{
    public long OrderListId { get; set; }
    public string ContingencyType { get; set; } = string.Empty;
    public string ListStatusType { get; set; } = string.Empty;
    public List<BinanceOrderResponse> Orders { get; set; } = [];
}

#endregion
