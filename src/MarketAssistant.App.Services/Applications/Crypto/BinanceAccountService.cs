using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 币安账户服务（需要鉴权）
/// 示例：如何使用BinanceAuthService进行鉴权调用
/// </summary>
public class BinanceAccountService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BinanceAccountService> _logger;
    private readonly BinanceAuthService _authService;
    public BinanceAccountService(
        IHttpClientFactory httpClientFactory,
        ILogger<BinanceAccountService> logger,
        BinanceAuthService authService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// 获取账户信息（需要USER_DATA权限）
    /// API文档：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api/account-endpoints#account-information-user_data
    /// </summary>
    public async Task<BinanceAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. 准备请求参数（本接口无额外参数，只需要签名）
            var signedQuery = _authService.SignQueryString("");

            // 2. 构建完整URL
            var url = $"/api/v3/account?{signedQuery}";

            // 3. 创建HTTP请求并添加鉴权Header
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            _authService.AddAuthHeaders(request);

            // 4. 发送请求
            _logger.LogInformation("正在获取币安账户信息...");
            using var httpClient = _httpClientFactory.CreateClient("Binance");
            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var accountInfo = await response.Content.ReadFromJsonAsync<BinanceAccountInfo>(cancellationToken);

            if (accountInfo == null)
            {
                throw new FriendlyException("解析账户信息失败");
            }

            _logger.LogInformation("成功获取账户信息，账户类型: {AccountType}", accountInfo.AccountType);
            return accountInfo;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取账户信息失败 - 网络错误");
            throw new FriendlyException("获取账户信息失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取账户信息失败");
            throw new FriendlyException($"获取账户信息失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 下单示例（需要TRADE权限）
    /// API文档：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api/market-data-endpoints#place-new-order-trade
    /// </summary>
    /// <param name="symbol">交易对，如BTCUSDT</param>
    /// <param name="side">买卖方向：BUY或SELL</param>
    /// <param name="type">订单类型：LIMIT、MARKET等</param>
    /// <param name="quantity">数量</param>
    /// <param name="price">价格（限价单需要）</param>
    public async Task<BinanceOrderResponse> PlaceOrderAsync(
        string symbol,
        string side,
        string type,
        decimal quantity,
        decimal? price = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. 构建请求参数
            // 注意：数量/价格必须使用 InvariantCulture 格式化，否则在逗号小数区域（如 de-DE）
            // 会生成 "1,23456789"，币安 API 返回 -1100 非法字符错误。
            var parameters = new Dictionary<string, string>
            {
                ["symbol"] = symbol.ToUpper(),
                ["side"] = side.ToUpper(),
                ["type"] = type.ToUpper(),
                ["quantity"] = quantity.ToString("F8", CultureInfo.InvariantCulture)
            };

            // 限价单需要价格和timeInForce
            if (type.ToUpper() == "LIMIT")
            {
                if (!price.HasValue)
                {
                    throw new ArgumentException("限价单必须指定价格");
                }
                parameters["price"] = price.Value.ToString("F8", CultureInfo.InvariantCulture);
                parameters["timeInForce"] = "GTC"; // Good Till Cancel
            }

            // 2. 将参数转换为query string格式
            var queryString = string.Join("&",
                parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

            // 3. 签名
            var signedQuery = _authService.SignQueryString(queryString);

            // 4. 构建请求
            var url = $"/api/v3/order?{signedQuery}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            _authService.AddAuthHeaders(request);

            _logger.LogInformation("正在下单: {Symbol} {Side} {Type} 数量:{Quantity}",
                symbol, side, type, quantity);

            using var httpClient = _httpClientFactory.CreateClient("Binance");
            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            // 6. 解析响应
            var orderResponse = await response.Content.ReadFromJsonAsync<BinanceOrderResponse>(cancellationToken);

            if (orderResponse == null)
            {
                throw new FriendlyException("解析订单响应失败");
            }

            _logger.LogInformation("下单成功，订单ID: {OrderId}, 状态: {Status}",
                orderResponse.OrderId, orderResponse.Status);

            return orderResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "下单失败 - 网络错误");
            throw new FriendlyException("下单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "下单失败");
            throw new FriendlyException($"下单失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 查询订单状态（需要USER_DATA权限）
    /// </summary>
    public async Task<BinanceOrderResponse> GetOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryString = $"symbol={symbol.ToUpper()}&orderId={orderId}";
            var signedQuery = _authService.SignQueryString(queryString);
            var url = $"/api/v3/order?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            _authService.AddAuthHeaders(request);

            using var httpClient = _httpClientFactory.CreateClient("Binance");
            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var orderResponse = await response.Content.ReadFromJsonAsync<BinanceOrderResponse>(cancellationToken)
                ?? throw new FriendlyException("解析订单信息失败");

            return orderResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "查询订单失败 - 网络错误");
            throw new FriendlyException("查询订单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "查询订单失败");
            throw new FriendlyException($"查询订单失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 取消订单（需要TRADE权限）
    /// </summary>
    public async Task<BinanceOrderResponse> CancelOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryString = $"symbol={symbol.ToUpper()}&orderId={orderId}";
            var signedQuery = _authService.SignQueryString(queryString);
            var url = $"/api/v3/order?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            _authService.AddAuthHeaders(request);

            _logger.LogInformation("正在取消订单: {Symbol} OrderId:{OrderId}", symbol, orderId);

            using var httpClient = _httpClientFactory.CreateClient("Binance");
            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var orderResponse = await response.Content.ReadFromJsonAsync<BinanceOrderResponse>(cancellationToken)
                ?? throw new FriendlyException("解析取消订单响应失败");

            _logger.LogInformation("取消订单成功，订单ID: {OrderId}", orderId);
            return orderResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "取消订单失败 - 网络错误");
            throw new FriendlyException("取消订单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "取消订单失败");
            throw new FriendlyException($"取消订单失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 查询当前挂单（需要USER_DATA权限）
    /// </summary>
    public async Task<List<BinanceOrderResponse>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryString = string.IsNullOrEmpty(symbol) ? "" : $"symbol={symbol.ToUpper()}";
            var signedQuery = _authService.SignQueryString(queryString);
            var url = $"/api/v3/openOrders?{signedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            _authService.AddAuthHeaders(request);

            using var httpClient = _httpClientFactory.CreateClient("Binance");
            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<BinanceOrderResponse>>(cancellationToken) ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "查询挂单失败 - 网络错误");
            throw new FriendlyException("查询挂单失败: 网络连接错误", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "查询挂单失败");
            throw new FriendlyException($"查询挂单失败: {ex.Message}", ex);
        }
    }
}

#region 响应模型

/// <summary>
/// 币安账户信息
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
