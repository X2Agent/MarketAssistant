using System.Text.Json;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 根据模型能力配置结构化输出。Schema 统一由 Microsoft.Extensions.AI 生成。
/// </summary>
public static class StructuredOutputOptions
{
    public static ChatResponseFormat? CreateResponseFormat(
        Type resultType,
        StructuredOutputMode mode,
        JsonSerializerOptions? serializerOptions = null)
    {
        return mode switch
        {
            StructuredOutputMode.JsonSchema => ChatResponseFormat.ForJsonSchema(resultType, serializerOptions),
            StructuredOutputMode.JsonObject => ChatResponseFormat.Json,
            _ => null
        };
    }

    public static string AppendSchemaInstructions(
        string instructions,
        Type resultType,
        StructuredOutputMode mode,
        JsonSerializerOptions? serializerOptions = null)
    {
        if (mode == StructuredOutputMode.JsonSchema)
            return instructions;

        var schema = AIJsonUtilities.CreateJsonSchema(
            resultType,
            description: null,
            hasDefaultValue: false,
            defaultValue: null,
            serializerOptions);

        return $$"""
            {{instructions}}

            ## 结构化输出要求
            仅返回一个符合下方 JSON Schema 的合法 JSON 对象。
            不得输出 Markdown 代码块、解释文字、思考过程或 JSON 对象之外的内容。

            JSON Schema:
            {{schema.GetRawText()}}
            """;
    }
}
