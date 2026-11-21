namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 交易账户只读查询工具（余额、持仓、订单状态）的 DI 分发标记接口（仅 Crypto 市场注册），
/// 用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。接口名沿用历史命名，实现类不得提供下单、撤单等写操作：
/// 写操作只允许经 TradeExecutor 统一风控执行，不暴露给模型。
/// 本身不定义业务方法，具体 API 由市场实现类提供（CryptoTradingExecutionTools）。
/// </summary>
public interface ITradingExecutionTools : IToolsProvider
{
}
