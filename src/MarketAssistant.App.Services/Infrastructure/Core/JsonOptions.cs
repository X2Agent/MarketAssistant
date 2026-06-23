using System.Text.Encodings.Web;
using System.Text.Json;
using MarketAssistant.Services.Data;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 共享的 JsonSerializerOptions 实例，避免在多处重复定义相同配置
/// </summary>
public static class JsonOptions
{
    /// <summary>
    /// 用于 A 股 API 响应反序列化：属性名大小写不敏感 + 字符串数值自动转换为 decimal/decimal?
    /// </summary>
    public static readonly JsonSerializerOptions AShareApiOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new StringToDecimalConverter() }
    };

    /// <summary>
    /// 用于资产数据格式化输出：缩进排版 + 非转义中文输出
    /// </summary>
    public static readonly JsonSerializerOptions AssetFormatterOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
