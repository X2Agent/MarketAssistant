using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 从大模型返回文本中稳健提取并反序列化 JSON 的通用工具。
///
/// 某些 LLM 提供商不支持请求级 JSON Schema，或即使请求了结构化输出仍会在 JSON 前后
/// 输出多余文本（前缀词、思考过程、markdown 代码块标记等）。本工具通过多层兜底策略
/// 定位并解析真正的 JSON 片段，避免因首字符非 <c>{</c>/<c>[</c> 而解析失败。
/// </summary>
public static class LlmJsonExtractor
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// 从 LLM 返回文本中提取并反序列化为指定类型。
    /// 解析顺序：直接解析 → 剥离 markdown 代码块 → 用 <see cref="Utf8JsonReader"/> 精确定位 JSON 边界。
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="rawText">LLM 返回的原始文本</param>
    /// <param name="options">JSON 序列化选项，为 null 时使用默认 Web 选项</param>
    /// <returns>反序列化后的对象</returns>
    /// <exception cref="JsonException">所有兜底策略均失败时抛出，包含原始文本预览与内部异常</exception>
    public static T? Deserialize<T>(string? rawText, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return default;

        var jsonOptions = options ?? JsonSerializerOptions.Web;
        Exception? lastException = null;

        // 第一层：直接解析（最理想情况，LLM 严格遵守了 JSON Schema 输出）
        if (TryDeserialize<T>(rawText, jsonOptions, out var result1, out var ex1))
            return result1;
        lastException = ex1;

        // 第二层：剥离 markdown 代码块（```json ... ``` 或 ``` ... ```）
        var stripped = StripMarkdownCodeFence(rawText);
        if (!ReferenceEquals(stripped, rawText))
        {
            if (TryDeserialize<T>(stripped, jsonOptions, out var result2, out var ex2))
                return result2;
            if (ex2 != null) lastException = ex2;
        }

        // 第三层：用 Utf8JsonReader 精确定位首个完整 JSON 值的边界
        // 相比 IndexOf('{') 启发式，能正确处理字符串内的 {、嵌套结构、转义字符等
        Exception? ex3 = null;
        if (TryExtractJsonWithReader(rawText, out var jsonText) &&
            TryDeserialize<T>(jsonText, jsonOptions, out var result3, out ex3))
            return result3;
        if (ex3 != null) lastException = ex3;

        // 所有策略失败：抛出带原始文本预览与最后一次异常的异常，便于排查
        var preview = rawText.Length > 500 ? rawText[..500] : rawText;
        throw new JsonException(
            $"LLM 返回文本无法解析为 JSON。原始文本前 500 字符: {preview}", lastException);
    }

    /// <summary>
    /// 仅提取 JSON 文本片段（不反序列化）。供需要自行反序列化的调用方使用。
    /// </summary>
    /// <returns>提取到的 JSON 文本；若所有策略均失败则返回 null。</returns>
    public static string? ExtractJsonString(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return rawText;

        if (IsValidJson(rawText))
            return rawText;

        var stripped = StripMarkdownCodeFence(rawText);
        if (!ReferenceEquals(stripped, rawText) && IsValidJson(stripped))
            return stripped;

        if (TryExtractJsonWithReader(rawText, out var jsonText))
            return jsonText;

        return null;
    }

    private static bool TryDeserialize<T>(
        string text, JsonSerializerOptions options,
        out T? result, out Exception? exception)
    {
        try
        {
            result = JsonSerializer.Deserialize<T>(text, options);
            exception = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            result = default;
            exception = ex;
            return false;
        }
    }

    /// <summary>
    /// 剥离 markdown 代码块标记。若文本不包含代码块则原样返回（引用相等）。
    /// </summary>
    private static string StripMarkdownCodeFence(string text)
    {
        var span = text.AsSpan().Trim();
        if (!span.StartsWith("```"))
            return text;

        var firstNewline = span.IndexOf('\n');
        if (firstNewline < 0)
            return text;

        var contentStart = firstNewline + 1;
        var fenceEnd = span.LastIndexOf("```");
        if (fenceEnd <= contentStart)
            return text;

        return span[contentStart..fenceEnd].Trim().ToString();
    }

    /// <summary>
    /// 使用 <see cref="Utf8JsonReader"/> 逐 token 扫描，精确定位首个完整 JSON 值的边界。
    /// 相比 <c>IndexOf('{')</c> 启发式，能正确处理：
    /// - JSON 字符串值内出现的 { } [ ]
    /// - 嵌套对象/数组
    /// - 转义字符
    /// </summary>
    private static bool TryExtractJsonWithReader(string text, out string json)
    {
        json = text;

        // 找到首个 { 或 [ 作为 JSON 起始
        var startIndex = text.AsSpan().IndexOfAny('{', '[');
        if (startIndex < 0)
            return false;

        // 只需将子串转为 UTF-8 字节一次，reader 直接在这些字节上工作
        var jsonBytes = Encoding.UTF8.GetBytes(text[startIndex..]);
        var reader = new Utf8JsonReader(jsonBytes, ReaderOptions);

        try
        {
            // 读取首个 token（必须是 StartObject 或 StartArray）
            if (!reader.Read())
                return false;

            if (reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
                return false;

            // 持续读取直到回到初始深度（即读完首个完整 JSON 值）
            var depth = 0;
            do
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        depth++;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        depth--;
                        break;
                }

                if (depth == 0)
                    break;

                if (!reader.Read())
                    return false;
            } while (true);

            // 直接从已转换的子串字节中截取 JSON 部分，无需再次编码完整文本
            json = Encoding.UTF8.GetString(jsonBytes, 0, (int)reader.BytesConsumed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
