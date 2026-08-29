namespace MarketAssistant.Services.Trading;

/// <summary>
/// 市场监控器提供者：延迟解析 <see cref="MarketMonitor"/>，
/// 打破 TradingEnvironmentService → MarketMonitor → BinanceUserDataStreamService → TradingEnvironmentService 的循环依赖。
/// </summary>
public interface IMarketMonitorProvider
{
    MarketMonitor GetMonitor();
}
