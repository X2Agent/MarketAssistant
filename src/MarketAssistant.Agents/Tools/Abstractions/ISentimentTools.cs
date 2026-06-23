namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 市场情绪工具的 DI 分发标记接口，用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。
/// 本身不定义业务方法，具体 API 由市场专用子接口提供。
/// </summary>
/// <remarks>
/// 市场专用实现：
/// - A 股：<see cref="IShareSentimentTools"/>（资金流向）
/// - 虚拟币：<see cref="ICryptoSentimentTools"/>（资金费率、多空比、持仓量等）
/// </remarks>
public interface ISentimentTools : IToolsProvider
{
}
