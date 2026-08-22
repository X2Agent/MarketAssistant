namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 结构化任务使用的响应格式能力。
/// </summary>
public enum StructuredOutputMode
{
    /// <summary>
    /// 不发送 response_format，仅通过提示词约束输出结构。
    /// </summary>
    Text,

    /// <summary>
    /// 请求合法 JSON 对象，并通过提示词约束具体结构。
    /// </summary>
    JsonObject,

    /// <summary>
    /// 向服务端发送完整 JSON Schema。
    /// </summary>
    JsonSchema
}
