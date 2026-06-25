using MarketAssistant.Trading.Models;

namespace MarketAssistant.Trading.Abstractions;

/// <summary>
/// 交易所客户端抽象接口
/// 解耦交易模块与具体交易所实现，支持未来接入 OKX、Bybit 等
/// </summary>
public interface IExchangeClient
{
    /// <summary>
    /// 交易所名称标识
    /// </summary>
    string ExchangeName { get; }

    /// <summary>
    /// 获取账户余额
    /// </summary>
    Task<ExchangeAccountInfo> GetAccountInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// 对交易标的下单
    /// </summary>
    /// <param name="clientOrderId">客户端自定义订单 ID，用于网络重试时实现幂等性，避免重复下单</param>
    Task<ExchangeOrderResult> PlaceOrderAsync(
        string instrumentSymbol, OrderSide side, OrderType type,
        decimal quantity, decimal? price = null,
        string? clientOrderId = null,
        CancellationToken ct = default);

    /// <summary>
    /// 查询订单状态
    /// </summary>
    Task<ExchangeOrderResult> GetOrderAsync(
        string instrumentSymbol, string orderId, CancellationToken ct = default);

    /// <summary>
    /// 取消订单
    /// </summary>
    Task<ExchangeOrderResult> CancelOrderAsync(
        string instrumentSymbol, string orderId, CancellationToken ct = default);

    /// <summary>
    /// 查询指定交易标的或全部挂单
    /// </summary>
    Task<List<ExchangeOrderResult>> GetOpenOrdersAsync(
        string? instrumentSymbol = null, CancellationToken ct = default);
}

/// <summary>
/// 交易所账户信息（统一模型）
/// </summary>
public class ExchangeAccountInfo
{
    public bool CanTrade { get; set; }
    public List<ExchangeBalance> Balances { get; set; } = [];
}

/// <summary>
/// 交易所余额（统一模型）
/// </summary>
public class ExchangeBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
}

/// <summary>
/// 交易所订单结果（统一模型）
/// </summary>
public class ExchangeOrderResult
{
    public string Symbol { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal RequestedQty { get; set; }
    public decimal ExecutedQty { get; set; }
    public decimal Price { get; set; }

    /// <summary>
    /// 成交手续费（以 <see cref="CommissionAsset"/> 计价）。
    /// 交易所未返回时为 0。
    /// </summary>
    public decimal FillCommission { get; set; }

    /// <summary>
    /// 手续费币种（如 BNB、USDT、BTC）。交易所未返回时为空。
    /// </summary>
    public string? CommissionAsset { get; set; }
}
