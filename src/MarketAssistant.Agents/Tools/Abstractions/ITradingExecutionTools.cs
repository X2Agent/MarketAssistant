namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 交易执行工具的 DI 分发标记接口（仅 Crypto 市场注册），用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。
/// 本身不定义业务方法，具体 API 由市场实现类提供（CryptoTradingExecutionTools）。
/// </summary>
public interface ITradingExecutionTools : IToolsProvider
{
}
