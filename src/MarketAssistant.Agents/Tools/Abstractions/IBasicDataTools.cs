namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 基础数据工具的 DI 分发标记接口，用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。
/// 本身不定义业务方法，具体 API 由市场实现类提供（A股：AShareBasicTools；虚拟币：CryptoBasicTools）。
/// </summary>
public interface IBasicDataTools : IToolsProvider
{
}





