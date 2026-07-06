using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安账户服务基类，封装现货/合约共用的 HTTP 调用、签名、错误处理与双层 catch 模板。
/// 子类只需提供端点前缀与响应映射逻辑。
/// positionSide 参数仅在合约下单时使用，现货下单忽略。
/// </summary>
public abstract class BinanceAccountServiceBase
{
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;
    protected readonly IBinanceAuthService AuthService;
    protected readonly string HttpClientName;
    protected readonly string Label;

    /// <param name="httpClientFactory">HttpClient 工厂</param>
    /// <param name="logger">日志器</param>
    /// <param name="authService">鉴权服务（决定实盘/Testnet 密钥来源）</param>
    /// <param name="httpClientName">HttpClient 名称（"Binance" / "BinanceSpotTestnet" / "BinanceFutures" / "BinanceFuturesTestnet"）</param>
    /// <param name="label">错误提示前缀（"" / "Testnet "）</param>
    protected BinanceAccountServiceBase(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        IBinanceAuthService authService,
        string httpClientName,
        string label)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        Logger = logger;
        AuthService = authService;
        HttpClientName = httpClientName;
        Label = label;
    }

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
    /// 获取账户信息。子类通过 <see cref="ParseAccountInfoAsync"/> 提供响应解析。
    /// </summary>
    public async Task<BinanceAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        try
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
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Label}获取账户信息失败 - 网络错误", Label);
            throw new FriendlyException($"{Label}获取账户信息失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            Logger.LogError(ex, "{Label}获取账户信息失败", Label);
            throw new FriendlyException($"{Label}获取账户信息失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 下单。先校验账户可交易，再按现货/合约端点签名调用。子类通过 <see cref="AppendPlaceOrderParameters"/> 注入特定参数。
    /// </summary>
    public async Task<BinanceOrderResponse> PlaceOrderAsync(
        string symbol,
        string side,
        string type,
        decimal quantity,
        decimal? price = null,
        string? clientOrderId = null,
        string? positionSide = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountInfo = await GetAccountInfoAsync(cancellationToken);
            if (!accountInfo.CanTrade)
            {
                throw new FriendlyException($"{Label}账户当前被限制交易，无法下单（可能因违规、KYC 未完成或地区限制）");
            }

            var parameters = new Dictionary<string, string>
            {
                ["symbol"] = symbol.ToUpper(),
                ["side"] = side.ToUpper(),
                ["type"] = type.ToUpper(),
                ["quantity"] = quantity.ToString("F8", CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrEmpty(clientOrderId))
                parameters["newClientOrderId"] = clientOrderId;

            AppendPlaceOrderParameters(parameters, positionSide);

            if (type.ToUpper() == "LIMIT")
            {
                if (!price.HasValue)
                {
                    throw new ArgumentException("限价单必须指定价格");
                }
                parameters["price"] = price.Value.ToString("F8", CultureInfo.InvariantCulture);
                parameters["timeInForce"] = "GTC";
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
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Label}下单失败 - 网络错误", Label);
            throw new FriendlyException($"{Label}下单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            Logger.LogError(ex, "{Label}下单失败", Label);
            throw new FriendlyException($"{Label}下单失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 查询订单状态。
    /// </summary>
    public async Task<BinanceOrderResponse> GetOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryString = $"symbol={symbol.ToUpper()}&orderId={orderId}";
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"{OrderEndpoint}?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AuthService.AddAuthHeaders(request);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}查询订单", cancellationToken);

            return await ParseOrderResponseAsync(response.Content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Label}查询订单失败 - 网络错误", Label);
            throw new FriendlyException($"{Label}查询订单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            Logger.LogError(ex, "{Label}查询订单失败", Label);
            throw new FriendlyException($"{Label}查询订单失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 撤销订单。
    /// </summary>
    public async Task<BinanceOrderResponse> CancelOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryString = $"symbol={symbol.ToUpper()}&orderId={orderId}";
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
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Label}取消订单失败 - 网络错误", Label);
            throw new FriendlyException($"{Label}取消订单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            Logger.LogError(ex, "{Label}取消订单失败", Label);
            throw new FriendlyException($"{Label}取消订单失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 查询当前挂单。
    /// </summary>
    public async Task<List<BinanceOrderResponse>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryString = string.IsNullOrEmpty(symbol) ? "" : $"symbol={symbol.ToUpper()}";
            var signedQuery = await AuthService.SignQueryStringAsync(queryString, cancellationToken);
            var url = $"{OpenOrdersEndpoint}?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AuthService.AddAuthHeaders(request);

            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithBinanceErrorAsync(response, $"{Label}查询挂单", cancellationToken);

            return await ParseOrderListResponseAsync(response.Content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Label}查询挂单失败 - 网络错误", Label);
            throw new FriendlyException($"{Label}查询挂单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            Logger.LogError(ex, "{Label}查询挂单失败", Label);
            throw new FriendlyException($"{Label}查询挂单失败: {ex.Message}", ex);
        }
    }

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
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<BinanceApiError>(content);
            errorDetail = error != null && !string.IsNullOrEmpty(error.Msg)
                ? $"[{error.Code}] {error.Msg}"
                : (string.IsNullOrEmpty(content) ? $"HTTP {statusCode}" : content);
        }
        catch
        {
            errorDetail = $"HTTP {statusCode}";
        }

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
public sealed class BinanceSpotAccountService : BinanceAccountServiceBase
{
    public BinanceSpotAccountService(
        IHttpClientFactory httpClientFactory,
        ILogger<BinanceSpotAccountService> logger,
        IBinanceAuthService authService,
        string httpClientName,
        string label)
        : base(httpClientFactory, logger, authService, httpClientName, label)
    {
    }

    protected override string AccountEndpoint => "/api/v3/account";
    protected override string OrderEndpoint => "/api/v3/order";
    protected override string OpenOrdersEndpoint => "/api/v3/openOrders";

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
    public List<BinanceBalance> Balances { get; set; } = new();
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
    public string OrigQty { get; set; } = string.Empty;
    public string ExecutedQty { get; set; } = string.Empty;
    public string CummulativeQuoteQty { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TimeInForce { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;

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

#endregion
