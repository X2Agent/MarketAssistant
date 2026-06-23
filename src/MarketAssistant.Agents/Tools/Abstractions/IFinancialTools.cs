namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 财务/市场数据工具的 DI 分发标记接口，用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。
/// 本身不定义业务方法，具体 API 由市场专用子接口提供。
/// </summary>
/// <remarks>
/// 市场专用实现：
/// - A 股：<see cref="IShareFinancialTools"/>（财务报表、财务指标）
/// - 虚拟币：<see cref="ICryptoMetricsTools"/>（市场深度、波动率、OHLCV 指标）
/// </remarks>
public interface IFinancialTools : IToolsProvider
{
}
