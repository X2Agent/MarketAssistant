using MarketAssistant.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json.Serialization;

namespace MarketAssistant.Parsers;

/// <summary>
/// AI驱动的分析师数据解析器 - 使用AI插件解析分析师返回内容
/// </summary>
public class AIAnalystDataParser : IAnalystDataParser
{
    private readonly Kernel _kernel;
    private readonly KernelFunction _parseFunction;

    /// <summary>
    /// 初始化AI分析师数据解析器
    /// </summary>
    /// <param name="kernel">语义内核实例</param>
    public AIAnalystDataParser(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));

        // 加载AI解析插件
        var yamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Agents", "Plugins", "Yaml", "analyst_data_parser.yaml");
        if (!File.Exists(yamlPath))
        {
            throw new FileNotFoundException($"AI解析插件配置文件未找到: {yamlPath}");
        }

        var promptYaml = File.ReadAllText(yamlPath);
        _parseFunction = _kernel.CreateFunctionFromPromptYaml(promptYaml);
    }

    /// <summary>
    /// 异步解析分析师返回的文本内容
    /// </summary>
    /// <param name="content">分析师返回的文本内容</param>
    /// <returns>解析后的结构化数据</returns>
    public async Task<AnalystResult> ParseDataAsync(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return new AnalystResult();
        }

        try
        {
            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
                ResponseFormat = "json_object",
                Temperature = 0.1,
                MaxTokens = 4000
            };

            // 设置AI解析参数
            var arguments = new KernelArguments(promptExecutionSettings)
            {
                ["content"] = content
            };

            // 调用AI解析插件
            var result = await _parseFunction.InvokeAsync(_kernel, arguments);
            var jsonResult = result.GetValue<string>();

            // 解析JSON结果
            if (string.IsNullOrEmpty(jsonResult))
            {
                return new AnalystResult();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                Converters = { new FlexibleStringConverter() }
            };
            return JsonSerializer.Deserialize<AnalystResult>(jsonResult, options) ?? new AnalystResult();
        }
        catch (Exception ex)
        {
            // AI解析失败时返回空结果，避免程序崩溃
            Console.WriteLine($"AI解析失败: {ex.Message}");
            return new AnalystResult();
        }
    }
}

/// <summary>
/// 灵活的字符串转换器，用于处理JSON中各种类型到字符串的转换
/// </summary>
public class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDouble().ToString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => reader.GetString()
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}