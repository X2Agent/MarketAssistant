using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 无链上数据市场（A 股）的空实现：无任何工具函数，
/// 分析师即使声明 [RequiresTools(typeof(IOnChainTools))] 也不会获得链上工具，
/// 工厂对"接口已注册但无函数"与"接口缺失"均正常降级，不产生告警噪音。
/// </summary>
public sealed class NoopOnChainTools : IOnChainTools
{
    public IEnumerable<AIFunction> GetFunctions() => [];
}