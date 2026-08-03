using MarketAssistant.Agents.PromptConfiguration;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 为 JsonObject 结构化输出生成 JSON Schema 提示词。
/// API 层由调用方使用 <see cref="ChatResponseFormat.Json"/> 保证合法 JSON，
/// 提示词约束对象结构，解析侧由 <c>LlmJsonExtractor</c> 负责容错提取。
/// </summary>
public static class StructuredOutputHelper
{
    /// <summary>
    /// 为指定结果类型生成 JSON Schema 提示词。
    /// </summary>
    public static string BuildSchemaPromptSection(Type resultType, string schemaName)
    {
        var schema = AIJsonUtilities.CreateJsonSchema(resultType);
        var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });

        return $"""
               ## JSON 输出格式要求

               仅返回一个符合下列 JSON Schema 的合法 JSON 对象。
               回复必须以 JSON 对象的左大括号开始、以右大括号结束，不得输出 JSON 对象之外的任何内容。

               JSON Schema（{schemaName}）：
               ```json
               {schemaJson}
               ```

               必须遵守：
               - 仅输出一个 JSON 对象
               - 不要添加解释、标题、前言或结尾
               - 不要使用 Markdown 代码块包裹最终结果
               - 字段名称和字段类型必须符合上述 JSON Schema
               - 所有必填字段必须存在并具有有效值
               - 枚举字段必须使用 JSON Schema 规定的值
               """;
    }

    /// <summary>
    /// 将 schema 描述注入到 AnalystPromptConfig 的 Instructions 中，返回新实例。
    /// </summary>
    public static AnalystPromptConfig MergeSchemaPrompt(AnalystPromptConfig config, Type resultType)
    {
        var schemaPrompt = BuildSchemaPromptSection(resultType, resultType.Name);
        return config.WithInstructions(config.Instructions + "\n\n" + schemaPrompt);
    }
}
