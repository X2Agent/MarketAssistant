using System.ComponentModel;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 顶层市场类型枚举
/// </summary>
public enum MarketType
{
    /// <summary>
    /// A股市场
    /// </summary>
    [Description("A股")]
    AShare,

    /// <summary>
    /// 虚拟币市场
    /// </summary>
    [Description("虚拟币")]
    Crypto
}






