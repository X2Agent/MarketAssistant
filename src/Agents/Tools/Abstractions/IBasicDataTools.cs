using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 基础数据工具基接口
/// </summary>
/// <remarks>
/// 为不同市场的基础数据工具提供统一抽象：
/// - A股市场：实现为 IShareBasicTools（股票行情和公司信息）
/// - 虚拟币市场：实现为 ICryptoBasicTools（币行情和项目信息）
/// </remarks>
public interface IBasicDataTools
{
    /// <summary>
    /// 获取AI工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}





