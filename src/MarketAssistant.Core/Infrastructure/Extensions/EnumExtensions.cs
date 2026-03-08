using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;

namespace MarketAssistant.Infrastructure.Extensions;

/// <summary>
/// 枚举扩展方法
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// 获取枚举的 Description 属性值
    /// </summary>
    public static string GetDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        if (field == null)
            return value.ToString();

        var attribute = field.GetCustomAttribute<DescriptionAttribute>();
        return attribute?.Description ?? value.ToString();
    }
}
