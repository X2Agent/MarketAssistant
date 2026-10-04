namespace MarketAssistant.Agents.Analysts.Attributes;

/// <summary>
/// 声明分析师所需的工具接口类型
/// Factory 通过此 Attribute 自动解析并注入对应的市场特定工具实现
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class RequiresToolsAttribute(Type toolInterfaceType) : Attribute
{
    public Type ToolInterfaceType { get; } = toolInterfaceType ?? throw new ArgumentNullException(nameof(toolInterfaceType));
}
