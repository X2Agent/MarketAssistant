using MarketAssistant.Trading.Models;

namespace MarketAssistant.Trading.Abstractions;

/// <summary>
/// 交易所客户端工厂：按交易模式构建对应的交易所客户端实例，
/// 使组合根与路由层不依赖具体交易所实现（P1-5）。
/// </summary>
public interface IExchangeClientFactory
{
    /// <summary>构建指定交易模式的交易所客户端。返回的实例由调用方持有生命周期。</summary>
    IExchangeClient Create(CryptoTradingMode tradingMode);
}
