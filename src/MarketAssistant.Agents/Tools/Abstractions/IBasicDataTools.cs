namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 基础数据工具的 DI 分发标记接口，用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。
/// 本身不定义业务方法，具体 API 由市场专用子接口提供。
/// </summary>
/// <remarks>
/// 市场专用实现：
/// - A 股：<see cref="IShareBasicTools"/>（股票行情、公司信息）
/// - 虚拟币：<see cref="ICryptoBasicTools"/>（币行情、项目信息）
/// </remarks>
public interface IBasicDataTools : IToolsProvider
{
}





