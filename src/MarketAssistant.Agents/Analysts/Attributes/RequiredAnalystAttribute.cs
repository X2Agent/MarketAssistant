using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Agents.Analysts.Attributes;

/// <summary>
/// 标记该分析师为必需角色（用户不可关闭），但仍受 <see cref="SupportedMarketsAttribute"/> 市场过滤约束。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RequiredAnalystAttribute : Attribute
{
}
