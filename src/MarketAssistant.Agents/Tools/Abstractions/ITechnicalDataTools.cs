namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 技术分析数据工具的 DI 分发标记接口，用于 <c>[RequiresTools]</c> 声明和 Keyed DI 注册。
/// 本身不定义业务方法，具体 API 由市场实现类提供（AShareTechnicalTools / CryptoTechnicalTools），
/// 经实现类的 [Description] 方法与 GetFunctions() 暴露给模型。
/// </summary>
public interface ITechnicalDataTools : IToolsProvider
{
}
