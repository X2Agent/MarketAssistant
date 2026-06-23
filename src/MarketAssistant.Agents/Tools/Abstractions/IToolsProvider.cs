using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 工具提供者基接口，统一暴露 AI 工具函数列表
/// </summary>
public interface IToolsProvider
{
    /// <summary>
    /// 获取 AI 工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}
