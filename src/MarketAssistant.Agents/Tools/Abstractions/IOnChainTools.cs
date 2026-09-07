using MarketAssistant.Agents.Tools.Abstractions;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 链上数据工具的 DI 分发标记接口，用于 [RequiresTools] 声明和 Keyed DI 注册。
/// 本身不定义业务方法，具体 API 由市场实现类提供（虚拟币：CryptoOnChainTools）。
/// </summary>
public interface IOnChainTools : IToolsProvider
{
}