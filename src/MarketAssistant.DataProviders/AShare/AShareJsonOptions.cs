using System.Text.Json;

namespace MarketAssistant.DataProviders;

/// <summary>
/// A 股数据客户端共享的 JSON 反序列化配置：
/// 属性名大小写不敏感 + 字符串数值/null/-- 占位容错转换为 decimal。
/// </summary>
public static class AShareJsonOptions
{
    /// <summary>A 股 API 响应反序列化选项。</summary>
    public static readonly JsonSerializerOptions Instance = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new StringToDecimalConverter() }
    };
}