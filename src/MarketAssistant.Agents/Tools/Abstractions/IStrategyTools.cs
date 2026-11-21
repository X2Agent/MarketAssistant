namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 策略只读查询工具的 DI 分发标记接口，用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。
/// 实现类不得提供修改策略状态、创建护栏等写操作（写入只由系统完成，不暴露给模型）。
/// 本身不定义业务方法，具体 API 由市场实现类提供（CryptoStrategyTools 等）。
/// </summary>
public interface IStrategyTools : IToolsProvider
{
}
