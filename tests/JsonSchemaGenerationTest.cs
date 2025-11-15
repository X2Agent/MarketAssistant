using MarketAssistant.Applications.StockSelection.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace TestMarketAssistant;

/// <summary>
/// 测试 AIJsonUtilities.CreateJsonSchema 如何处理各种 DataAnnotations 特性
/// 用于验证 JSON Schema 的生成逻辑
/// </summary>
[TestClass]
public class JsonSchemaGenerationTest
{
    /// <summary>
    /// 测试 StockSelectionResult 的 JSON Schema 生成
    /// </summary>
    [TestMethod]
    public void Test_StockSelectionResult_JsonSchema_Generation()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("测试 StockSelectionResult 的 JSON Schema");
        Console.WriteLine("========================================\n");

        // 生成 JSON Schema
        var schema = AIJsonUtilities.CreateJsonSchema(
            typeof(StockSelectionResult),
            serializerOptions: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

        var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        Console.WriteLine(schemaJson);
        Console.WriteLine("\n========================================");
        Console.WriteLine("关键检查项：");
        Console.WriteLine("========================================");

        // 检查是否包含关键字段
        var checks = new Dictionary<string, bool>
        {
            ["包含 $schema"] = schemaJson.Contains("\"$schema\""),
            ["包含 description"] = schemaJson.Contains("\"description\""),
            ["包含 minLength"] = schemaJson.Contains("\"minLength\""),
            ["包含 maxLength"] = schemaJson.Contains("\"maxLength\""),
            ["包含 minimum"] = schemaJson.Contains("\"minimum\""),
            ["包含 maximum"] = schemaJson.Contains("\"maximum\""),
        };

        foreach (var check in checks)
        {
            Console.WriteLine($"✓ {check.Key}: {(check.Value ? "是" : "否")}");
        }

        Console.WriteLine("\n");
    }
}
